using Gaia.Models;
using Gaia.Services;

namespace Turtle.Contract.Models;

public class TurtlePostResponse : IValidationErrors
{
    public List<ValidationError> ValidationErrors { get; set; } = [];
}