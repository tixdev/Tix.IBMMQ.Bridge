using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tix.IBMMQ.Bridge.Options;
using Tix.IBMMQ.Bridge.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<MQBridgeOptions>(builder.Configuration.GetSection("MQBridge"));
builder.Services.AddHostedService<MQBridgeService>();

var app = builder.Build();

app.Run();
