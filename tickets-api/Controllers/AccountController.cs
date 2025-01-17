using System.Net.Mime;
using Microsoft.AspNetCore.Mvc;
using TicketsApi.AppConfig.Errors;
using TicketsApi.Dtos;
using TicketsApi.Interfaces.Repositories;

namespace TicketsApi.Controllers;

[ApiController]
[Route("accounts")]
public class AccountController(IUserAccountService userAccountService) : ControllerBase
{
    [HttpPost("register")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Register(UserDto userDto)
    {
        try
        {
            var response = await userAccountService.CreateAccount(userDto);
            return new ObjectResult(response.message) { StatusCode = StatusCodes.Status201Created };
        }
        catch (UnprocessedEntityException e)
        {
            return new ObjectResult(new { message = e.Message }) 
            { StatusCode = StatusCodes.Status422UnprocessableEntity};
        }
        catch (BadRequestException e)
        {
            return new ObjectResult(new { message = e.Message }) 
            { StatusCode = StatusCodes.Status400BadRequest};
        }
    }

    [HttpPost("login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginDto loginDTO)
    {
        try
        {
            var response = await userAccountService.LoginAccount(loginDTO);
            return Ok(response);
        }
        catch (NotFoundException e)
        {
            return new ObjectResult(new { message = e.Message }) 
            { StatusCode = StatusCodes.Status404NotFound};
        } 
        catch (UnauthorizationException e)
        {
            return new ObjectResult(new { message = e.Message }) 
            { StatusCode = StatusCodes.Status401Unauthorized};
        }
    }
}
