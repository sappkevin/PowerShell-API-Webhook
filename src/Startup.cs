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
            
            services.AddControllers();
            
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
            
            // Register services as singletons for better performance in high-traffic scenarios
            services.AddSingleton<IScriptRunnerService, ScriptRunner>();
            services.AddSingleton<IHandlerDispatcher, HandlerDispatcher>();
            services.AddSingleton<IScriptValidationService, ScriptValidationService>();

            // Register validators
            // The order is matter, if the first validator fails
            // the service return validation errors and stop further validation.
            // This was made like that because in some cases when validator 1 is failed
            // then it does not make sense to run the validator 2 because it might depend on the 1st one.
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

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
            });
        }
    }
}