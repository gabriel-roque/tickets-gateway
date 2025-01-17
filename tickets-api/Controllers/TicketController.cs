using System.Net.Mime;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TicketsApi.AppConfig.Errors;
using TicketsApi.Dtos;
using TicketsApi.Enums;
using TicketsApi.Interfaces.Services;
using TicketsApi.Models;
using TicketsApi.ViewModels;

namespace TicketsApi.Controllers;

[ApiController]
[Route("tickets")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class TicketController (
    ITicketService ticketService,
    IMapper mapper
    ) : ControllerBase
{
    
    [HttpGet("")]
    [Authorize(Roles = $"{Roles.USER}, {Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<Ticket>>> GetTickets(int skip, int take)
    {
        var userId = User.GetUserId();
        IEnumerable<Ticket> tickets = await ticketService.ListByOwner(userId, skip, take);
        
        return Ok(Ticket.ToListView(tickets));
    }
    
    [HttpPost("")]
    [Authorize(Roles = $"{Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TicketViewModel>> CreateTicket([FromBody] TicketCreateDto body)
    {
        try
        {
            var userId = User.GetUserId();
            Ticket ticket = new Ticket()
            {
                EventId = body.EventId,
                OwnerId = userId,
                PaymentMethod = body.PaymentMethod
            };
            
            await ticketService.Create(ticket);
            return new ObjectResult(Ticket.ToGetView(ticket)) { StatusCode = StatusCodes.Status201Created };
        }
        catch (Exception e)
        {
            return BadRequest(e);
        }
    }
}
