﻿using Microsoft.AspNetCore.Http.HttpResults;
using CardType = eShop.Ordering.API.Application.Queries.CardType;
using Order = eShop.Ordering.API.Application.Queries.Order;

public static class OrdersApi
{
    public static RouteGroupBuilder MapOrdersApiV1(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("api/orders").HasApiVersion(1.0);

        api.MapPut("/cancel", CancelOrderAsync);
        api.MapPut("/ship", ShipOrderAsync);
        api.MapGet("{orderId:int}", GetOrderAsync);
        api.MapGet("/", GetOrdersByUserAsync);
        api.MapGet("/cardtypes", GetCardTypesAsync);
        api.MapPost("/draft", CreateOrderDraftAsync);
        api.MapPost("/", CreateOrderAsync);
        api.MapGet("/tradegecko/orders", GetTradeGeckoOrders)
            .WithName("GetTradeGeckoOrders")
            .WithSummary("Get orders from TradeGecko")
            .WithDescription("Fetch orders from TradeGecko API");
        api.MapGet("/tradegecko/orders/{orderId:int}", GetTradeGeckoOrder)
            .WithName("GetTradeGeckoOrder")
            .WithSummary("Get order from TradeGecko")
            .WithDescription("Fetch a specific order from TradeGecko API");
        api.MapPost("/tradegecko/orders", CreateTradeGeckoOrder)
            .WithName("CreateTradeGeckoOrder")
            .WithSummary("Create order in TradeGecko")
            .WithDescription("Create a new order in TradeGecko API");
        api.MapPut("/tradegecko/orders/{orderId:int}/status", UpdateTradeGeckoOrderStatus)
            .WithName("UpdateTradeGeckoOrderStatus")
            .WithSummary("Update order status in TradeGecko")
            .WithDescription("Update financial and fulfillment status for an order in TradeGecko");

        return api;
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> CancelOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CancelOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestCancelOrder = new IdentifiedCommand<CancelOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestCancelOrder.GetGenericTypeName(),
            nameof(requestCancelOrder.Command.OrderNumber),
            requestCancelOrder.Command.OrderNumber,
            requestCancelOrder);

        var commandResult = await services.Mediator.Send(requestCancelOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Cancel order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok, BadRequest<string>, ProblemHttpResult>> ShipOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        ShipOrderCommand command,
        [AsParameters] OrderServices services)
    {
        if (requestId == Guid.Empty)
        {
            return TypedResults.BadRequest("Empty GUID is not valid for request ID");
        }

        var requestShipOrder = new IdentifiedCommand<ShipOrderCommand, bool>(command, requestId);

        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            requestShipOrder.GetGenericTypeName(),
            nameof(requestShipOrder.Command.OrderNumber),
            requestShipOrder.Command.OrderNumber,
            requestShipOrder);

        var commandResult = await services.Mediator.Send(requestShipOrder);

        if (!commandResult)
        {
            return TypedResults.Problem(detail: "Ship order failed to process.", statusCode: 500);
        }

        return TypedResults.Ok();
    }

    public static async Task<Results<Ok<Order>, NotFound>> GetOrderAsync(int orderId, [AsParameters] OrderServices services)
    {
        try
        {
            var order = await services.Queries.GetOrderAsync(orderId);
            return TypedResults.Ok(order);
        }
        catch
        {
            return TypedResults.NotFound();
        }
    }

    public static async Task<Ok<IEnumerable<OrderSummary>>> GetOrdersByUserAsync([AsParameters] OrderServices services)
    {
        var userId = services.IdentityService.GetUserIdentity();
        var orders = await services.Queries.GetOrdersFromUserAsync(userId);
        return TypedResults.Ok(orders);
    }

    public static async Task<Ok<IEnumerable<CardType>>> GetCardTypesAsync(IOrderQueries orderQueries)
    {
        var cardTypes = await orderQueries.GetCardTypesAsync();
        return TypedResults.Ok(cardTypes);
    }

    public static async Task<OrderDraftDTO> CreateOrderDraftAsync(CreateOrderDraftCommand command, [AsParameters] OrderServices services)
    {
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
            command.GetGenericTypeName(),
            nameof(command.BuyerId),
            command.BuyerId,
            command);

        return await services.Mediator.Send(command);
    }

    public static async Task<Results<Ok, BadRequest<string>>> CreateOrderAsync(
        [FromHeader(Name = "x-requestid")] Guid requestId,
        CreateOrderRequest request,
        [AsParameters] OrderServices services)
    {
        
        //mask the credit card number
        
        services.Logger.LogInformation(
            "Sending command: {CommandName} - {IdProperty}: {CommandId}",
            request.GetGenericTypeName(),
            nameof(request.UserId),
            request.UserId); //don't log the request as it has CC number

        if (requestId == Guid.Empty)
        {
            services.Logger.LogWarning("Invalid IntegrationEvent - RequestId is missing - {@IntegrationEvent}", request);
            return TypedResults.BadRequest("RequestId is missing.");
        }

        using (services.Logger.BeginScope(new List<KeyValuePair<string, object>> { new("IdentifiedCommandId", requestId) }))
        {
            var maskedCCNumber = request.CardNumber.Substring(request.CardNumber.Length - 4).PadLeft(request.CardNumber.Length, 'X');
            var createOrderCommand = new CreateOrderCommand(request.Items, request.UserId, request.UserName, request.City, request.Street,
                request.State, request.Country, request.ZipCode,
                maskedCCNumber, request.CardHolderName, request.CardExpiration,
                request.CardSecurityNumber, request.CardTypeId);

            var requestCreateOrder = new IdentifiedCommand<CreateOrderCommand, bool>(createOrderCommand, requestId);

            services.Logger.LogInformation(
                "Sending command: {CommandName} - {IdProperty}: {CommandId} ({@Command})",
                requestCreateOrder.GetGenericTypeName(),
                nameof(requestCreateOrder.Id),
                requestCreateOrder.Id,
                requestCreateOrder);

            var result = await services.Mediator.Send(requestCreateOrder);

            if (result)
            {
                services.Logger.LogInformation("CreateOrderCommand succeeded - RequestId: {RequestId}", requestId);
            }
            else
            {
                services.Logger.LogWarning("CreateOrderCommand failed - RequestId: {RequestId}", requestId);
            }

            return TypedResults.Ok();
        }
    }

    public static async Task<Ok<List<Models.TradeGeckoOrder>>> GetTradeGeckoOrders(
        [AsParameters] OrderServices services,
        [System.ComponentModel.Description("Number of orders to return")] int? limit,
        [System.ComponentModel.Description("Page number for pagination")] int? page)
    {
        var tradeGeckoService = services.ServiceProvider.GetRequiredService<Services.ITradeGeckoService>();
        var orders = await tradeGeckoService.GetOrdersAsync(limit, page);
        return TypedResults.Ok(orders);
    }

    public static async Task<Results<Ok<Models.TradeGeckoOrder>, NotFound>> GetTradeGeckoOrder(
        [AsParameters] OrderServices services,
        [System.ComponentModel.Description("The TradeGecko order ID")] int orderId)
    {
        var tradeGeckoService = services.ServiceProvider.GetRequiredService<Services.ITradeGeckoService>();
        var order = await tradeGeckoService.GetOrderAsync(orderId);
        if (order == null)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(order);
    }

    public static async Task<Results<Ok<Models.TradeGeckoOrder>, BadRequest<string>>> CreateTradeGeckoOrder(
        [AsParameters] OrderServices services,
        Models.TradeGeckoOrder order)
    {
        var tradeGeckoService = services.ServiceProvider.GetRequiredService<Services.ITradeGeckoService>();
        var createdOrder = await tradeGeckoService.CreateOrderAsync(order);
        if (createdOrder == null)
        {
            return TypedResults.BadRequest("Failed to create order in TradeGecko");
        }
        return TypedResults.Ok(createdOrder);
    }

    public static async Task<Results<Ok<bool>, NotFound>> UpdateTradeGeckoOrderStatus(
        [AsParameters] OrderServices services,
        [System.ComponentModel.Description("The TradeGecko order ID")] int orderId,
        [System.ComponentModel.Description("The financial status")] string financialStatus,
        [System.ComponentModel.Description("The fulfillment status")] string fulfillmentStatus)
    {
        var tradeGeckoService = services.ServiceProvider.GetRequiredService<Services.ITradeGeckoService>();
        var success = await tradeGeckoService.UpdateOrderStatusAsync(orderId, financialStatus, fulfillmentStatus);
        if (!success)
        {
            return TypedResults.NotFound();
        }
        return TypedResults.Ok(success);
    }
}

public record CreateOrderRequest(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    List<BasketItem> Items);
