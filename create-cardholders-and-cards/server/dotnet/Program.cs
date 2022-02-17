using Microsoft.AspNetCore.StaticFiles.Infrastructure;
using Microsoft.Extensions.FileProviders;
using Stripe;
using Stripe.Issuing;
using CardCreateOptions = Stripe.Issuing.CardCreateOptions;
using CardService = Stripe.Issuing.CardService;

// For sample support and debugging, not required for production:
StripeConfiguration.AppInfo = new AppInfo
{
    Name = "stripe-samples/issuing/create-cardholder-and-card",
    Url = "https://github.com/stripe-samples",
    Version = "0.0.1",
};

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

var stripeKey = builder.Configuration["STRIPE_SECRET_KEY"];
var webHookSigningSecret = builder.Configuration["STRIPE_WEBHOOK_SECRET"];

builder.Services.AddSingleton(new StripeClient(stripeKey));
builder.Services.AddSingleton<CardholderService>();
builder.Services.AddSingleton<CardService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

var staticFileOptions = new SharedOptions()
{
    FileProvider = new PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), @"../../client")
        )
};

app.UseDefaultFiles(new DefaultFilesOptions(staticFileOptions));
app.UseStaticFiles(new StaticFileOptions(staticFileOptions));

app.MapPost("/create-cardholder", async (CreateCardholderRequest req, ILogger<Program> logger, CardholderService service) =>
{
    try
    {
        var options = new CardholderCreateOptions
        {
            Type = "individual",
            Name = req.Name,
            Email = req.Email,
            PhoneNumber = req.PhoneNumber,
            Billing = new CardholderBillingOptions
            {
                Address = new AddressOptions
                {
                    Line1 = req.Line1,
                    City = req.City,
                    State = req.State,
                    Country = req.Country,
                    PostalCode = req.PostalCode,
                },
            },
        };
        var cardholder = await service.CreateAsync(options);
        return Results.Ok(cardholder);
    }
    catch (StripeException e)
    {
        logger.LogError(e, "API Call to Stripe failed.");
        return Results.BadRequest(new { error = new { message = e.StripeError.Message } });
    }
});

app.MapPost("/create-card", async (CreateCardRequest req, ILogger<Program> logger) =>
{
    try
    {
        var options = new CardCreateOptions
        {
            Cardholder = req.Cardholder,
            Currency = req.Currency,
            Type = "virtual",
            Status = req.Status,
        };
        var service = new CardService();
        var card = await service.CreateAsync(options);
        return Results.Ok(card);
    }
    catch (StripeException e)
    {
        logger.LogError(e, "API Call to Stripe failed.");
        return Results.BadRequest(new { error = new { message = e.StripeError.Message } });
    }
});

app.MapGet("/cards/{id}", async (string id, CardService service, ILogger<Program> logger) =>
{
    try
    {
        var card = await service.GetAsync(id);
        return Results.Ok(card);
    }
    catch (StripeException e)
    {
        logger.LogError(e, "A$PI Call to Stripe failed.");
        return Results.BadRequest(new { error = new { message = e.StripeError.Message } });
    }
});

app.MapPost("/webhook", async (HttpRequest request, ILogger<Program> logger) =>
{
    var json = await new StreamReader(request.Body).ReadToEndAsync();
    try
    {
        var stripeEvent = EventUtility.ConstructEvent(
            json,
            request.Headers["Stripe-Signature"],
            webHookSigningSecret
        );

        switch (stripeEvent.Type)
        {
            // Handle the event
            case Events.IssuingCardholderCreated:
                logger.LogInformation("Cardholder created!");
                break;
            case Events.IssuingCardCreated:
                logger.LogInformation("Card created!");
                break;
            default:
                logger.LogWarning("Unhandled event type: {0}", stripeEvent.Type);
                break;
        }

        return Results.Ok(new { Message = "success" });
    }
    catch (StripeException e)
    {
        logger.LogError(e, "A$PI Call to Stripe failed.");
        return Results.BadRequest(new { Error = e.Message });
    }
});

app.Run();