using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SistemaPermisos.Data;
using SistemaPermisos.Middleware;
using SistemaPermisos.Services;
using System;

#nullable enable

var builder = WebApplication.CreateBuilder(args);

// Configurar servicios
builder.Services.AddControllersWithViews();

// Configurar sesiones
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Configurar la conexi�n a la base de datos
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Registrar IHttpContextAccessor antes de los servicios que lo utilizan
builder.Services.AddHttpContextAccessor();

// Registrar servicios
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IExportService, ExportService>();

var app = builder.Build();

// Configurar el pipeline de solicitudes HTTP
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Usar sesiones
app.UseSession();

// Middleware personalizado para manejo de excepciones globales
app.UseMiddleware<ErrorHandlingMiddleware>();

// Middleware personalizado para verificar la autenticaci�n (SIMPLIFICADO)
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

    // Rutas que no requieren autenticaci�n
    var publicPaths = new[] {
        "/account/login",
        "/account/forgotpassword",
        "/account/resetpassword",
        "/home/error",
        "/account/accessdenied"
    };

    // Verificar si la ruta actual es p�blica o si contiene archivos est�ticos
    var isPublicPath = Array.Exists(publicPaths, p => path.StartsWith(p)) ||
                       path.StartsWith("/lib/") ||
                       path.StartsWith("/css/") ||
                       path.StartsWith("/js/") ||
                       path.StartsWith("/images/") ||
                       path.StartsWith("/favicon");

    // Si es una ruta p�blica, continuar sin verificar autenticaci�n
    if (isPublicPath)
    {
        await next();
        return;
    }

    // Verificar si el usuario est� autenticado (usando sesi�n)
    var usuarioId = context.Session.GetInt32("UsuarioId");

    if (usuarioId == null)
    {
        // Solo redirigir si no estamos ya en la p�gina de login
        if (!path.StartsWith("/account/login"))
        {
            context.Response.Redirect("/Account/Login");
            return;
        }
    }

    await next();
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// Asegurar que la base de datos est� creada y las migraciones aplicadas
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Crear usuario administrador por defecto
        await SeedData.Initialize(services);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating or seeding the database.");
    }
}

app.Run();
