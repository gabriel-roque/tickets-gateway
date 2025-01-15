using AutoMapper;
using TicketsApi.Dtos;
using TicketsApi.Models;

namespace TicketsApi.AppConfig;

public class MappingProfile: Profile
{
    public MappingProfile()
    {
        CreateMap<EventCreateDto, Event>();
        CreateMap<EventUpdateDto, Event>();
    }
}