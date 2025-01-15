using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TicketsApi.Middlewares;

public class RateLimitMiddleware : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Anywhere logic example
        if (false)
        {
            // TODO: implement logic
            context.Result = new StatusCodeResult(403);
            return;
        }

        base.OnActionExecuting(context);
    }
}