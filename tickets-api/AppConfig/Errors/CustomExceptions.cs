namespace TicketsApi.AppConfig.Errors;

public class UnauthorizationException(string? message) : Exception
{
    public string Message = message;
}

public class NotFoundException(string? message) : Exception
{
    public string Message = message;
}

public class UnprocessedEntityException(string? message) : Exception
{
    public string Message = message;
}

public class BadRequestException(string? message) : Exception
{
    public string Message = message;
}

