using Gaia.Errors;
using Gaia.Services;

namespace Turtle.Contract.Models;

public class TurtlePostResponse : IValidationErrors
{
    public ValidationError[] ValidationErrors { get; set; } = [];
}