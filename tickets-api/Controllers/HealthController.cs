using System.Net.Mime;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketsApi.Dtos;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;
using TicketsApi.ViewModels;

namespace TicketsApi.Controllers;

[ApiController]
[Route("health")]
public class HealthController (
    ) : ControllerBase
{
    
    [HttpGet("")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<OkResult> Health(int skip, int take)
    {
        return Ok();
    }
}
