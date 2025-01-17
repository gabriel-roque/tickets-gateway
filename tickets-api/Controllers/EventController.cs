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

namespace TicketsApi.Controllers;

[ApiController]
[Route("events")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public class EventController (
    IEventService eventService,
    IMapper mapper
    ) : ControllerBase
{
    
    [HttpGet("")]
    [Authorize(Roles = $"{Roles.USER}, {Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<Event>>> GetEvents(int skip, int take)
    {
        IEnumerable<Event> events = await eventService.List(skip, take);
        return Ok(Event.ToListView(events));
    }
    
    [HttpGet("{id}")]
    [Authorize(Roles = $"{Roles.USER}, {Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Event>> GetEvent([FromRoute] string id)
    {
        try
        {
            Guid.TryParse(id, out var eventId);
            Event @event = await eventService.Get(eventId);
    
            if (@event is null) return NotFound();
    
            return Ok(@event.ToGetView());
        }
        catch (Exception e)
        {
            return BadRequest(e);
        }
    }
    
    [HttpPost("")]
    [Authorize(Roles = $"{Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EventViewModel>> CreateEvent([FromBody] EventCreateDto body)
    {
        Event @event = mapper.Map<Event>(body);
    
        try
        {
            var userId = User.GetUserId();
            @event.OwnerId = userId.ToString();
            
            await eventService.Create(@event);
            return new ObjectResult(@event.ToGetView()) { StatusCode = StatusCodes.Status201Created };
        }
        catch (Exception e)
        {
            return BadRequest(e);
        }
    }
    
    [HttpPut("{id}")]
    [Authorize(Roles = $"{Roles.ADMIN}")]
    [Consumes(MediaTypeNames.Application.Json)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Event>> UpdateEvent([FromBody] EventUpdateDto body, string id)
    {
        Event @event = mapper.Map<Event>(body);
        @event.Id = Guid.Parse(id);
    
        try
        {
            await eventService.Update(@event);
            return new ObjectResult(null) { StatusCode = StatusCodes.Status204NoContent };
        }
        catch (NotFoundException e)
        {
            return new ObjectResult(new { message = e.Message }) 
            { StatusCode = StatusCodes.Status404NotFound};
        } 
        catch (DbUpdateConcurrencyException e)
        {
            return new ObjectResult(new { message = "Concurrency Exception - Try again" }) 
            { StatusCode = StatusCodes.Status400BadRequest};
        } 
    }
}
