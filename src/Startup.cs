using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Webhookshell.Interfaces;
using Webhookshell.Options;
using Webhookshell.Services;
using Webhookshell.Validators;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace Webhookshell
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ScriptOptions>(options =>
            {
                Configuration.GetSection("Scripts").Bind(options);
            });
            
            services.AddControllers()
                .AddJsonOptions(options =>
                {
                    // Configure JSON serialization for better parameter handling
                    options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
                    options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep original casing
                });
            
            // Register Swagger
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo 
                { 
                    Title = "PowerShell API Webhook", 
                    Version = "v1",
                    Description = "A cross-platform API for executing PowerShell scripts via HTTP requests",
                    Contact = new OpenApiContact
                    {
                        Name = "GitHub Repository",
                        Url = new Uri("https://github.com/sappkevin/PowerShell-API-Webhook")
                    },
                    License = new OpenApiLicense
                    {
                        Name = "MIT License",
                        Url = new Uri("https://opensource.org/licenses/MIT")
                    }
                });
                
                // Set the comments path for the Swagger JSON and UI
                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                if (File.Exists(xmlPath))
                {
                    c.IncludeXmlComments(xmlPath);
                }
            });
            
            // Add Rate Limiting if enabled in configuration
            if (Configuration.GetValue<bool>("Performance:EnableRequestThrottling", false))
            {
                services.AddRateLimiter(options =>
                {
                    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    {
                        return RateLimitPartition.GetFixedWindowLimiter(
                            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
                            factory: _ => new FixedWindowRateLimiterOptions
                            {
                                AutoReplenishment = true,
                                PermitLimit = Configuration.GetValue<int>("Performance:MaxConcurrentRequests", 100),
                                QueueLimit = Configuration.GetValue<int>("Performance:RequestQueueLimit", 200),
                                Window = TimeSpan.FromSeconds(1)
                            });
                    });

                    options.OnRejected = async (context, token) =>
                    {
                        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                        await context.HttpContext.Response.WriteAsJsonAsync(new
                        {
                            Error = "Too many requests. Please try again later.",
                            RetryAfter = 1 // seconds
                        }, token);
                    };
                });
            }

            // Register services as singletons for better performance in high-traffic scenarios
            services.AddSingleton<IScriptRunnerService, ScriptRunner>();
            services.AddSingleton<IHandlerDispatcher, HandlerDispatcher>();
            services.AddSingleton<IScriptValidationService, ScriptValidationService>();

            // Register validators
            // The order matters - if the first validator fails, the service returns validation errors 
            // and stops further validation. This is designed this way because some validators may 
            // depend on others completing successfully first.
            services.AddSingleton<IScriptValidator, HttpTriggerValidator>();
            services.AddSingleton<IScriptValidator, IPAddressValidator>();
            services.AddSingleton<IScriptValidator, KeyValidator>();
            services.AddSingleton<IScriptValidator, TimeValidator>();
            
            // Register validators and helpers
            services.AddSingleton<ConfigurationValidator>();
            services.AddSingleton<DtoScriptValidator>();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseExceptionHandler("/error");
            
            // Enable Swagger and Swagger UI
            app.UseSwagger();
            app.UseSwaggerUI(c => 
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "PowerShell API Webhook v1");
                c.RoutePrefix = string.Empty; // Set Swagger UI at the root
            });
            
            app.UseHttpsRedirection();

            app.UseRouting();

            // Apply rate limiting if enabled
            if (Configuration.GetValue<bool>("Performance:EnableRequestThrottling", false))
            {
                app.UseRateLimiter();
            }

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}
