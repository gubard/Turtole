using Gaia.Errors;
using Gaia.Services;

namespace Turtle.Contract.Models;

public class TurtleGetResponse : IValidationErrors
{
    public Credential[] Roots { get; set; } = [];
    public Dictionary<Guid, Credential[]> Children { get; set; } = [];
    public Dictionary<Guid, Credential[]> Parents { get; set; } = [];
    public ValidationError[] ValidationErrors { get; set; } = [];
}