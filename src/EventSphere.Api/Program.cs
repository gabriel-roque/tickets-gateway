using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using EventSphere.Application;
using EventSphere.Domain;
using EventSphere.Infrastructure;
using EventSphere.Infrastructure.Persistence;
using EventSphere.Infrastructure.Messaging;
using EventSphere.Infrastructure.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks()
    .AddCheck<EventSphere.Infrastructure.Services.DependencyHealthCheck>("dependencies", tags: ["ready"]);
builder.Services.AddEventSphere(builder.Configuration);
var jwtKey = builder.Configuration["Security:JwtSigningKey"] ?? "development-signing-key-not-for-production-32";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["Security:JwtIssuer"] ?? "eventsphere",
        ValidateAudience = true,
        ValidAudience = builder.Configuration["Security:JwtAudience"] ?? "eventsphere-api",
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("customer", policy => policy.RequireRole("customer", "admin"))
    .AddPolicy("organizer", policy => policy.RequireRole("organizer", "admin"))
    .AddPolicy("gate", policy => policy.RequireRole("gate", "admin"));
var telemetry = builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("eventsphere"))
    .WithTracing(tracing => tracing
        .AddSource(EventSphereTelemetry.Name)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation())
    .WithMetrics(metrics => metrics
        .AddMeter(EventSphereTelemetry.Name)
        .AddAspNetCoreInstrumentation());
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    telemetry.WithTracing(tracing => tracing.AddOtlpExporter());
    telemetry.WithMetrics(metrics => metrics.AddOtlpExporter());
}

var role = builder.Configuration["EventSphere:Role"]?.ToLowerInvariant() ?? "all";
if (role is "all" or "saga") builder.Services.AddHostedService<SagaWorker>();
if (role is "all" or "lifecycle") builder.Services.AddHostedService<LifecycleWorkflowWorker>();
if (role is "all" or "expiry") builder.Services.AddHostedService<ReservationExpiryWorker>();
if (role is "all" or "projection") builder.Services.AddHostedService<ProjectionWorker>();
if (role is "all" or "outbox") builder.Services.AddHostedService<OutboxWorker>();
if ((role is "all" or "integration")
    && !string.IsNullOrWhiteSpace(builder.Configuration["Kafka:BootstrapServers"]))
    builder.Services.AddHostedService<KafkaInboxConsumer>();
if (role is "all" or "order-consumer")
    builder.Services.AddHostedService<KafkaOrderCommandConsumer>();

var app = builder.Build();

if (app.Environment.IsProduction()
    && string.IsNullOrWhiteSpace(builder.Configuration["Security:ApiKey"]))
    throw new InvalidOperationException("Security:ApiKey is required in Production.");
if (app.Environment.IsProduction()
    && string.IsNullOrWhiteSpace(builder.Configuration["Security:JwtSigningKey"]))
    throw new InvalidOperationException("Security:JwtSigningKey is required in Production.");

if (role is "migration" or "replay")
{
    await using var scope = app.Services.CreateAsyncScope();
    var write = scope.ServiceProvider.GetRequiredService<WriteDbContext>();
    var read = scope.ServiceProvider.GetRequiredService<ReadDbContext>();
    if (write.Database.IsRelational()) await write.Database.MigrateAsync();
    else await write.Database.EnsureCreatedAsync();
    if (read.Database.IsRelational()) await read.Database.MigrateAsync();
    else await read.Database.EnsureCreatedAsync();
    if (role == "replay")
        await scope.ServiceProvider.GetRequiredService<ProjectionEngine>().RebuildAsync(CancellationToken.None);
    return;
}

app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, title) = exception switch
    {
        AggregateNotFoundException => (StatusCodes.Status404NotFound, "Aggregate not found"),
        ExpectedVersionException => (StatusCodes.Status409Conflict, "Concurrency conflict"),
        IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency conflict"),
        DomainRuleViolationException => (StatusCodes.Status422UnprocessableEntity, "Domain rule violation"),
        _ => (StatusCodes.Status500InternalServerError, "Unexpected error")
    };
    context.Response.StatusCode = status;
    await Results.Problem(statusCode: status, title: title,
        detail: app.Environment.IsDevelopment() ? exception?.Message : null).ExecuteAsync(context);
}));

app.Use(async (context, next) =>
{
    var correlationHeader = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    context.Items["CorrelationId"] = Guid.TryParse(correlationHeader, out var correlationId) ? correlationId : Guid.NewGuid();
    context.Response.Headers["X-Correlation-Id"] = context.Items["CorrelationId"]!.ToString();
    await next(context);
});

app.Use(async (context, next) =>
{
    var configuredKey = builder.Configuration["Security:ApiKey"];
    var adminKey = builder.Configuration["Security:AdminApiKey"] ?? configuredKey;
    var isInternal = context.Request.Path.StartsWithSegments("/internal");
    var expectedKey = isInternal ? adminKey : configuredKey;
    if (isInternal && !string.IsNullOrWhiteSpace(expectedKey)
        && context.Request.Headers["X-Api-Key"] != expectedKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Invalid API key." });
        return;
    }
    await next(context);
});

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = check => check.Tags.Contains("ready") });

if (role is "all" or "command")
{
    app.MapPost("/api/v2/inventories", async (
        CreateInventoryCommand command,
        InventoryCommandService service,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        if (!CanManageShow(context, command.ShowId)) return Results.Forbid();
        var metadata = Metadata(context);
        var id = await service.CreateAsync(command, metadata, cancellationToken);
        return Results.Created($"/api/v2/inventories/{id}", new { inventoryId = id });
    }).RequireAuthorization("organizer");

    app.MapPost("/api/v2/orders", async (
        CreateOrderRequest request,
        IOrderIngress ingress,
        HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var actorId = ActorId(context);
        if (!context.User.IsInRole("admin") && request.CustomerId != actorId) return Results.Forbid();
        var customerId = context.User.IsInRole("admin") ? request.CustomerId : actorId;
        var idempotencyKey = context.Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.BadRequest(new { error = "Idempotency-Key header is required." });
        var accepted = await ingress.SubmitAsync(new CreateOrderCommand(
            customerId, request.InventoryId, request.Quantity, idempotencyKey),
            Metadata(context), cancellationToken);
        return Results.Accepted(accepted.StatusUrl, accepted);
    }).RequireAuthorization("customer");

    app.MapPost("/api/v2/tickets/{ticketId:guid}/transfer", async (
        Guid ticketId, TransferTicketRequest request, LifecycleSagaService service,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var workflowId = await service.StartTransferAsync(ticketId, request.NewOwnerId,
            ActorId(context), context.User.IsInRole("admin"), cancellationToken);
        return Results.Accepted($"/api/v2/workflows/{workflowId}", new { workflowId });
    }).RequireAuthorization("customer");

    app.MapPost("/api/v2/tickets/{ticketId:guid}/check-in", async (
        Guid ticketId, CheckInRequest request, TicketCommandService service,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        await service.CheckInAsync(ticketId, request.GateId, Metadata(context), cancellationToken);
        return Results.Accepted($"/api/v2/tickets/{ticketId}");
    }).RequireAuthorization("gate");

    app.MapPost("/api/v2/seats/lock", async (
        LockSeatRequest request, ISeatLockService service, HttpContext context,
        CancellationToken cancellationToken) =>
    {
        var command = new LockSeatCommand(ActorId(context), request.ShowId, request.Sector,
            request.Row, request.Number, request.ReservationId);
        await service.LockAsync(command, cancellationToken);
        return Results.Ok(new { request.ReservationId, locked = true });
    }).RequireAuthorization("customer");

    app.MapDelete("/api/v2/seats/lock/{reservationId:guid}", async (
        Guid reservationId, ISeatLockService service, HttpContext context,
        CancellationToken cancellationToken) =>
    {
        await service.ReleaseAsync(reservationId, ActorId(context), context.User.IsInRole("admin"), cancellationToken);
        return Results.NoContent();
    }).RequireAuthorization("customer");

    app.MapPost("/api/v2/orders/{orderId:guid}/refund", async (
        Guid orderId, RefundRequest request, LifecycleSagaService service,
        AggregateRepository aggregates, HttpContext context, CancellationToken cancellationToken) =>
    {
        var order = await aggregates.LoadAsync<OrderAggregate>(Streams.Order(orderId), cancellationToken);
        if (!CanAccessCustomerResource(context, order.CustomerId)) return Results.Forbid();
        var workflowId = await service.StartRefundAsync(orderId, request.Reason, cancellationToken);
        return Results.Accepted($"/api/v2/workflows/{workflowId}", new { workflowId });
    }).RequireAuthorization("customer");

    app.MapPost("/api/v2/shows/{showId:guid}/cancel", async (
        Guid showId, LifecycleSagaService service, HttpContext context,
        CancellationToken cancellationToken) =>
    {
        if (!CanManageShow(context, showId)) return Results.Forbid();
        var workflowId = await service.StartEventCancellationAsync(showId, cancellationToken);
        return Results.Accepted($"/api/v2/workflows/{workflowId}", new { workflowId });
    }).RequireAuthorization("organizer");
}

if (role is "all" or "query")
{
    app.MapGet("/api/v2/operations/{sagaId:guid}", async (
        Guid sagaId, ISagaRepository repository, HttpContext context, CancellationToken cancellationToken) =>
    {
        var saga = await repository.GetAsync(sagaId, cancellationToken);
        if (saga is null) return Results.NotFound();
        return CanAccessCustomerResource(context, saga.CustomerId) ? Results.Ok(saga) : Results.Forbid();
    }).RequireAuthorization("customer");

    app.MapGet("/api/v2/orders/{orderId:guid}", async (
        Guid orderId, IReadModelStore store, HttpContext context, CancellationToken cancellationToken) =>
    {
        var result = await store.GetOrderAsync(orderId, cancellationToken);
        if (result is null) return Results.NotFound();
        return CanAccessCustomerResource(context, result.CustomerId) ? Results.Ok(result) : Results.Forbid();
    }).RequireAuthorization("customer");

    app.MapGet("/api/v2/inventories/{inventoryId:guid}", async (
        Guid inventoryId, IReadModelStore store, CancellationToken cancellationToken) =>
    {
        var result = await store.GetInventoryAsync(inventoryId, cancellationToken);
        return result is null ? Results.NotFound() : Results.Ok(result);
    });

    app.MapGet("/api/v2/tickets/{ticketId:guid}", async (
        Guid ticketId, IReadModelStore store, HttpContext context, CancellationToken cancellationToken) =>
    {
        var result = await store.GetTicketAsync(ticketId, cancellationToken);
        if (result is null) return Results.NotFound();
        var allowed = CanAccessCustomerResource(context, result.OwnerId)
            || context.User.IsInRole("gate");
        return allowed ? Results.Ok(result) : Results.Forbid();
    }).RequireAuthorization();

    app.MapGet("/api/v2/workflows/{workflowId:guid}", async (
        Guid workflowId, ILifecycleWorkflowRepository repository, IReadModelStore readModels,
        HttpContext context, CancellationToken cancellationToken) =>
    {
        var result = await repository.GetAsync(workflowId, cancellationToken);
        if (result is null) return Results.NotFound();
        if (context.User.IsInRole("admin")) return Results.Ok(result);
        var allowed = result.Type switch
        {
            LifecycleWorkflowType.Refund => await readModels.GetOrderAsync(result.AggregateId, cancellationToken)
                is { } order && CanAccessCustomerResource(context, order.CustomerId),
            LifecycleWorkflowType.TicketTransfer => CanAccessCustomerResource(context, result.InitiatorId)
                || await readModels.GetTicketAsync(result.AggregateId, cancellationToken)
                    is { } ticket && CanAccessCustomerResource(context, ticket.OwnerId),
            LifecycleWorkflowType.EventCancellation => CanManageShow(context, result.AggregateId),
            _ => false
        };
        return allowed ? Results.Ok(result) : Results.Forbid();
    }).RequireAuthorization();
}

app.MapPost("/internal/projections/rebuild", async (
    ProjectionEngine engine, CancellationToken cancellationToken) =>
{
    await engine.RebuildAsync(cancellationToken);
    return Results.Accepted();
});

app.MapPost("/internal/sagas/{sagaId:guid}/retry", async (
    Guid sagaId, ISagaRepository repository, IClock clock, CancellationToken cancellationToken) =>
{
    var saga = await repository.GetAsync(sagaId, cancellationToken);
    if (saga is null) return Results.NotFound();
    var version = saga.Version;
    saga.Status = saga.Status == SagaStatus.Completed ? SagaStatus.Completed : SagaStatus.Running;
    saga.Attempts = 0;
    saga.LastError = null;
    saga.LeaseUntil = null;
    saga.NextAttemptAt = clock.UtcNow;
    await repository.SaveAsync(saga, version, cancellationToken);
    return Results.Accepted($"/api/v2/operations/{sagaId}");
});

app.MapPost("/internal/workflows/{workflowId:guid}/retry", async (
    Guid workflowId, ILifecycleWorkflowRepository repository, IClock clock, CancellationToken cancellationToken) =>
{
    var workflow = await repository.GetAsync(workflowId, cancellationToken);
    if (workflow is null) return Results.NotFound();
    var version = workflow.Version;
    workflow.Status = workflow.Status == LifecycleWorkflowStatus.Completed
        ? LifecycleWorkflowStatus.Completed : LifecycleWorkflowStatus.Running;
    workflow.Attempts = 0;
    workflow.LastError = null;
    workflow.LeaseUntil = null;
    workflow.NextAttemptAt = clock.UtcNow;
    await repository.SaveAsync(workflow, version, cancellationToken);
    return Results.Accepted($"/api/v2/workflows/{workflowId}");
});

app.MapPost("/internal/faults/payment", async (
    FaultWindow request, WriteDbContext database, IClock clock, CancellationToken cancellationToken) =>
{
    if (request.Seconds is < 0 or > 300) return Results.BadRequest(new { error = "Seconds must be between 0 and 300." });
    var fault = await database.FaultInjections.SingleOrDefaultAsync(x => x.Key == "payment", cancellationToken);
    if (fault is null)
    {
        fault = new FaultInjectionRecord { Key = "payment" };
        database.FaultInjections.Add(fault);
    }
    fault.Until = clock.UtcNow.AddSeconds(request.Seconds);
    await database.SaveChangesAsync(cancellationToken);
    return Results.Ok(new { fault.Key, fault.Until });
});

app.MapGet("/internal/audit/inventories/{inventoryId:guid}", async (
    Guid inventoryId,
    AggregateRepository aggregates,
    WriteDbContext database,
    CancellationToken cancellationToken) =>
{
    var inventory = await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), cancellationToken);
    var sagaCounts = await database.Sagas.AsNoTracking()
        .GroupBy(x => x.Status)
        .Select(group => new { status = ((SagaStatus)group.Key).ToString(), count = group.Count() })
        .ToListAsync(cancellationToken);
    var duplicatePayments = await database.Payments.AsNoTracking()
        .GroupBy(x => x.ExternalId).CountAsync(group => group.Count() > 1, cancellationToken);
    var duplicateTickets = await database.TicketIndex.AsNoTracking()
        .GroupBy(x => x.OrderId).CountAsync(group => group.Count() > 1, cancellationToken);
    return Results.Ok(new
    {
        inventoryId,
        inventory.Capacity,
        inventory.Reserved,
        inventory.Confirmed,
        inventory.Available,
        duplicatePayments,
        duplicateTickets,
        sagaCounts
    });
});

app.MapGet("/internal/audit/runs/{runId}", async (
    string runId,
    Guid showId,
    AggregateRepository aggregates,
    WriteDbContext writeDatabase,
    ReadDbContext readDatabase,
    CancellationToken cancellationToken) =>
{
    var show = await aggregates.LoadAsync<ShowAggregate>(Streams.Show(showId), cancellationToken);
    var inventoryIds = show.InventoryIds.ToList();
    var inventories = new List<InventoryAggregate>();
    foreach (var inventoryId in inventoryIds)
        inventories.Add(await aggregates.LoadAsync<InventoryAggregate>(Streams.Inventory(inventoryId), cancellationToken));

    var submissions = writeDatabase.Idempotency.AsNoTracking().Where(x => x.RawKey.StartsWith(runId + "-"));
    var sagasForRun = from saga in writeDatabase.Sagas.AsNoTracking()
                      join submission in submissions on saga.Id equals submission.SagaId
                      select saga;
    var ordersForRun = submissions.Select(x => x.OrderId);
    var sagaCounts = await sagasForRun.GroupBy(x => x.Status)
        .Select(group => new { status = ((SagaStatus)group.Key).ToString(), count = group.Count() })
        .ToListAsync(cancellationToken);
    var submitted = await submissions.CountAsync(cancellationToken);
    var payments = await writeDatabase.Payments.AsNoTracking().CountAsync(x => ordersForRun.Contains(x.OrderId), cancellationToken);
    var activePayments = await writeDatabase.Payments.AsNoTracking()
        .CountAsync(x => ordersForRun.Contains(x.OrderId) && x.Status == "Authorized", cancellationToken);
    var tickets = await writeDatabase.TicketIndex.AsNoTracking().CountAsync(x => ordersForRun.Contains(x.OrderId), cancellationToken);
    var activeTickets = await (from ticket in writeDatabase.TicketIndex.AsNoTracking()
                               join saga in sagasForRun on ticket.OrderId equals saga.OrderId
                               where saga.Status == (int)SagaStatus.Completed
                               select ticket).CountAsync(cancellationToken);
    var pendingOutbox = await writeDatabase.Outbox.AsNoTracking()
        .CountAsync(x => x.PublishedAt == null && x.DeadLetteredAt == null, cancellationToken);
    var deadLetters = await writeDatabase.Outbox.AsNoTracking().CountAsync(x => x.DeadLetteredAt != null, cancellationToken);
    var maxPosition = await writeDatabase.Events.AsNoTracking().MaxAsync(x => (long?)x.GlobalPosition, cancellationToken) ?? 0;
    var checkpoint = await readDatabase.Checkpoints.AsNoTracking()
        .Where(x => x.Projector == ProjectionEngine.ProjectorName)
        .Select(x => (long?)x.Position).SingleOrDefaultAsync(cancellationToken) ?? 0;
    var projected = await readDatabase.Inventories.AsNoTracking()
        .Where(x => inventoryIds.Contains(x.InventoryId))
        .GroupBy(_ => 1)
        .Select(x => new { capacity = x.Sum(y => y.Capacity), reserved = x.Sum(y => y.Reserved), confirmed = x.Sum(y => y.Confirmed), available = x.Sum(y => y.Available) })
        .SingleOrDefaultAsync(cancellationToken);
    return Results.Ok(new
    {
        runId,
        showId,
        buckets = inventories.Count,
        submitted,
        source = new
        {
            capacity = inventories.Sum(x => x.Capacity),
            reserved = inventories.Sum(x => x.Reserved),
            confirmed = inventories.Sum(x => x.Confirmed),
            available = inventories.Sum(x => x.Available)
        },
        projection = projected,
        sagaCounts,
        payments,
        activePayments,
        tickets,
        activeTickets,
        pendingOutbox,
        deadLetters,
        maxPosition,
        checkpoint,
        projectionLag = maxPosition - checkpoint
    });
});

app.Run();

static EventMetadata Metadata(HttpContext context)
{
    var correlationId = (Guid)(context.Items["CorrelationId"] ?? Guid.NewGuid());
    var causationHeader = context.Request.Headers["X-Causation-Id"].FirstOrDefault();
    var causationId = Guid.TryParse(causationHeader, out var parsed) ? parsed : correlationId;
    return new EventMetadata(correlationId, causationId,
        context.User.FindFirstValue("sub") ?? "system",
        Activity.Current?.TraceId.ToString() ?? string.Empty);
}

static Guid ActorId(HttpContext context)
{
    var subject = context.User.FindFirstValue("sub");
    return Guid.TryParse(subject, out var actorId)
        ? actorId
        : throw new DomainRuleViolationException("The authenticated subject must be a GUID.");
}

static bool CanAccessCustomerResource(HttpContext context, Guid ownerId) =>
    context.User.IsInRole("admin") || ActorId(context) == ownerId;

static bool CanManageShow(HttpContext context, Guid showId)
{
    if (context.User.IsInRole("admin")) return true;
    if (!context.User.IsInRole("organizer")) return false;
    var scope = context.User.FindAll("show_id").Select(x => x.Value);
    return scope.Any(value => value == "*" || Guid.TryParse(value, out var allowed) && allowed == showId);
}

public sealed record CreateOrderRequest(Guid CustomerId, Guid InventoryId, int Quantity);
public sealed record LockSeatRequest(Guid ShowId, string Sector, string Row, string Number, Guid ReservationId);
public sealed record TransferTicketRequest(Guid NewOwnerId);
public sealed record CheckInRequest(string GateId);
public sealed record RefundRequest(string Reason);
public sealed record FaultWindow(int Seconds);

public partial class Program;
