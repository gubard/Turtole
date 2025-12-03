using System.Collections.Frozen;
using System.Data.Common;
using Gaia.Errors;
using Gaia.Helpers;
using Gaia.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Nestor.Db.Helpers;
using Nestor.Db.Models;
using Turtle.Contract.Models;
using Turtle.Contract.Services;
using Turtle.Models;

namespace Turtle.Services;

public class CredentialService : ICredentialService
{
    private readonly DbContext _dbContext;

    public CredentialService(DbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async ValueTask<TurtleGetResponse> GetAsync(TurtleGetRequest request, CancellationToken ct)
    {
        var response = new TurtleGetResponse();
        var childrenIds = request.GetChildrenIds.Select(x => (Guid?)x).ToFrozenSet();
        var query = _dbContext.Set<EventEntity>().Where(x => x.Id == -1).Select(x => x.EntityId);

        if (request.IsGetRoots)
        {
            query = query.Concat(_dbContext.Set<EventEntity>().GetProperty(nameof(CredentialEntity), nameof(CredentialEntity.ParentId)).Where(x => x.EntityGuidValue == null).Distinct().Select(x => x.EntityId));
        }

        if (request.GetChildrenIds.Length != 0)
        {
            query = query.Concat(_dbContext.Set<EventEntity>().GetProperty(nameof(CredentialEntity), nameof(CredentialEntity.ParentId)).Where(x => childrenIds.Contains(x.EntityGuidValue)).Distinct().Select(x => x.EntityId));
        }

        if (request.GetParentsIds.Length != 0)
        {
            var parameters = CreateSqlRawParametersForAllChildren(request.GetParentsIds);
            query = query.Concat(_dbContext.Set<TempEntity>().FromSqlRaw(parameters.Sql, parameters.Parameters).Select(x => x.EntityId));
        }
    
        var credentials = await CredentialEntity.GetCredentialEntitysAsync(_dbContext.Set<EventEntity>().Where(x => query.Contains(x.EntityId)), ct);
        
        return response;
    }

    public async ValueTask<TurtlePostResponse> PostAsync(TurtlePostRequest request, CancellationToken ct)
    {
        var errors = new List<ValidationError>();
        var response = new TurtlePostResponse();
        await DeleteAsync(request.DeleteIds, ct);
        response.CreatedIds = await CreateAsync(request.CreateCredentials, ct);
        await EditAsync(request.EditCredentials, ct);
        await ChangeOrderAsync(request.ChangeOrders, errors, ct);
        await _dbContext.SaveChangesAsync(ct);
        response.ValidationErrors = errors.ToArray();

        return response;
    }

    private async ValueTask ChangeOrderAsync(ChangeOrder[] changeOrders, List<ValidationError> errors, CancellationToken ct)
    {
        if (changeOrders.Length == 0)
        {
            return;
        }

        var insertIds = changeOrders.SelectMany(x => x.InsertIds).Distinct().ToFrozenSet();
        var insertItems = await CredentialEntity.GetCredentialEntitysAsync(_dbContext.Set<EventEntity>().Where(x => insertIds.Contains(x.EntityId)), ct);
        var insertItemsDictionary = insertItems.ToDictionary(x => x.Id).ToFrozenDictionary();
        var startIds = changeOrders.Select(x => x.StartId).Distinct().ToFrozenSet();
        var startItems = await CredentialEntity.GetCredentialEntitysAsync(_dbContext.Set<EventEntity>().Where(x => startIds.Contains(x.EntityId)), ct);
        var startItemsDictionary = startItems.ToDictionary(x => x.Id).ToFrozenDictionary();
        var parentItems = startItems.Select(x => x.ParentId).Distinct().ToFrozenSet();
        var query = _dbContext.Set<EventEntity>().GetProperty(nameof(CredentialEntity), nameof(CredentialEntity.ParentId)).Where(x => parentItems.Contains(x.EntityGuidValue)).Select(x => x.EntityId).Distinct();
        var siblings = await CredentialEntity.GetCredentialEntitysAsync(_dbContext.Set<EventEntity>().Where(x => query.Contains(x.EntityId)), ct);
        var edits = new List<EditCredentialEntity>();

        for (var index = 0; index < changeOrders.Length; index++)
        {
            var changeOrder = changeOrders[index];

            var inserts = changeOrder.InsertIds.Select(x => insertItemsDictionary[x]).ToFrozenSet();

            if (!startItemsDictionary.TryGetValue(changeOrder.StartId, out var item))
            {
                errors.Add(new NotFoundValidationError(changeOrder.StartId.ToString()));

                continue;
            }

            var startIndex = changeOrder.IsAfter ? item.OrderIndex + 1 : item.OrderIndex;
            var items = siblings.Where(x => x.ParentId == item.ParentId).OrderBy(x => x.OrderIndex);

            var usedItems = changeOrder.IsAfter
                ? items.Where(x => x.OrderIndex > item.OrderIndex)
                : items.Where(x => x.OrderIndex >= item.OrderIndex);

            var newOrder = usedItems.Concat(inserts).ToFrozenSet();

            foreach (var newItem in newOrder)
            {
                edits.Add(new(newItem.Id)
                {
                    IsEditOrderIndex = startIndex != newItem.OrderIndex,
                    OrderIndex = startIndex++,
                    IsEditParentId = newItem.ParentId != item.ParentId,
                    ParentId = item.ParentId,
                });
            }
        }

        await CredentialEntity.EditCredentialEntitysAsync(_dbContext, "Service", edits.ToArray(), ct);
    }

    private ValueTask DeleteAsync(Guid[] ids, CancellationToken ct)
    {
        if (ids.Length == 0)
        {
            return ValueTask.CompletedTask;
        }

        return CredentialEntity.DeleteCredentialEntitysAsync(_dbContext, "Service", ct, ids);
    }

    private ValueTask EditAsync(EditCredential[] edits, CancellationToken ct)
    {
        if (edits.Length == 0)
        {
            return ValueTask.CompletedTask;
        }

        var editEntities = new Span<EditCredentialEntity>(new EditCredentialEntity[edits.Length]);

        for (var index = 0; index < edits.Length; index++)
        {
            var editCredential = edits[index];
            editEntities[index] = new(editCredential.Id)
            {
                CustomAvailableCharacters = editCredential.CustomAvailableCharacters,
                IsAvailableLowerLatin = editCredential.IsAvailableLowerLatin,
                IsAvailableNumber = editCredential.IsAvailableNumber,
                IsAvailableSpecialSymbols = editCredential.IsAvailableSpecialSymbols,
                IsAvailableUpperLatin = editCredential.IsAvailableUpperLatin,
                Key = editCredential.Key,
                Length = editCredential.Length,
                Login = editCredential.Login,
                Name = editCredential.Name,
                Regex = editCredential.Regex,
                Type = editCredential.Type,
                ParentId = editCredential.ParentId,
            };
        }

        return CredentialEntity.EditCredentialEntitysAsync(_dbContext, "Service", editEntities.ToArray(), ct);
    }

    private async ValueTask<Guid[]> CreateAsync(CreateCredential[] creates, CancellationToken ct)
    {
        if (creates.Length == 0)
        {
            return [];
        }

        var createdIds = new Span<Guid>(new Guid[creates.Length]);
        var entities = new Span<CredentialEntity>(new CredentialEntity[creates.Length]);

        for (var index = 0; index < creates.Length; index++)
        {
            var id = Guid.NewGuid();
            createdIds[index] = id;
            var createCredential = creates[index];
            entities[index] = new()
            {
                CustomAvailableCharacters = createCredential.CustomAvailableCharacters,
                IsAvailableLowerLatin = createCredential.IsAvailableLowerLatin,
                Id = id,
                IsAvailableNumber = createCredential.IsAvailableNumber,
                IsAvailableSpecialSymbols = createCredential.IsAvailableSpecialSymbols,
                IsAvailableUpperLatin = createCredential.IsAvailableUpperLatin,
                Key = createCredential.Key,
                Length = createCredential.Length,
                Login = createCredential.Login,
                Name = createCredential.Name,
                Regex = createCredential.Regex,
                Type = createCredential.Type,
                ParentId = createCredential.ParentId,
            };
        }

        var result = createdIds.ToArray();
        await CredentialEntity.AddCredentialEntitysAsync(_dbContext, "Service", ct, entities.ToArray());

        return result;
    }

    private SqlRawParameters CreateSqlRawParametersForAllChildren(ReadOnlyMemory<Guid> ids)
    {
        var idsString = Enumerable.Range(0, ids.Length).Select(i => $"@Id{i}").JoinString(", ");
        var parameters = new DbParameter[ids.Length];

        for (var i = 0; i < ids.Length; i++)
        {
            parameters[i] = new SqliteParameter($"@Id{i}", ids.Span[i]);
        }

        return new(
            $"""
             WITH RECURSIVE hierarchy(
                     Id,
                     EntityId,
                     EntityGuidValue
                  ) AS (
                      SELECT
                      Id,
                      EntityId,
                      EntityGuidValue
                      FROM EventEntity
                      WHERE EntityId IN ({idsString}) AND EntityProperty = 'ParentId' AND EntityType = 'CredentialEntity' AND Id IN (SELECT MAX(WHEN s.EntityProperty = 'ParentId' AND s.EntityType = 'CredentialEntity' THEN s.Id) FROM EventEntity AS s GROUP BY s.EntityId)

                      UNION ALL

                      SELECT
                      t.Id,
                      t.EntityId,
                      EntityGuidValue
                      FROM EventEntity t
                      WHERE EntityProperty = 'ParentId' AND EntityType = 'CredentialEntity' AND t.Id IN (SELECT MAX(WHEN e.EntityProperty = 'ParentId' AND e.EntityType = 'CredentialEntity' THEN e.Id) FROM EventEntity AS e GROUP BY e.EntityId)
                      INNER JOIN hierarchy h ON t.EntityGuidValue = h.EntityId
                  )
                  SELECT DISTINCT EntityId FROM hierarchy;
             """,
            parameters
        );
    }

    class TempEntity
    {
        public Guid EntityId { get; set; }
    }
}