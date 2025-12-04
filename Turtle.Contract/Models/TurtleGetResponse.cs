using Gaia.Errors;
using Gaia.Services;

namespace Turtle.Contract.Models;

public class TurtleGetResponse : IValidationErrors
{
    public List<Credential> Roots { get; set; } = [];
    public Dictionary<Guid, List<Credential>> Children { get; set; } = [];
    public Dictionary<Guid, List<Credential>> Parents { get; set; } = [];
    public ValidationError[] ValidationErrors { get; set; } = [];
}