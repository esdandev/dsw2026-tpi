using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Models;
using Dsw2026Tpi.CrossCutting.Resources;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace Dsw2026Tpi.Api.Middlewares;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            if (exception is DbUpdateException dbException && IsUniqueConstraintViolation(dbException))
            {
                _logger.LogWarning(exception,"Se rechazó una operación por violación de una restricción única");
            }
            else
            {
                _logger.LogError(exception, "Se produjo un error durante el procesamiento de la solicitud");
            }

            await HandleExceptionAsync(context, exception);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = ResolveStatusCode(exception);
        var error = ResolveError(exception);

        var result = JsonSerializer.Serialize(error, AppJsonOptions.Default);

        context.Response.ContentType = "application/json";

        context.Response.StatusCode = (int)statusCode;

        await context.Response.WriteAsync(result);
    }

    private static HttpStatusCode ResolveStatusCode(Exception exception)
    {
        return exception switch
        {
            DbUpdateException dbException when IsUniqueConstraintViolation(dbException)=> HttpStatusCode.Conflict,
            ValidationException => HttpStatusCode.BadRequest,
            EntityNotFoundException => HttpStatusCode.NotFound,
            BusinessRuleException => HttpStatusCode.Conflict,
            ConflictException => HttpStatusCode.Conflict,
            AuthenticationException => HttpStatusCode.Unauthorized,
            AuthorizationException => HttpStatusCode.Forbidden,
            _ => HttpStatusCode.InternalServerError
        };
    }

    private static ErrorResponse ResolveError(Exception exception)
    {
        if (exception is AppException appException)
            return appException.Error;
        

        if (exception is DbUpdateException dbException && IsUniqueConstraintViolation(dbException))
            return BuildUniqueConstraintError(dbException);
        

        return new ErrorResponse(nameof(ErrorCodes.UNHANDLED_ERROR), ErrorCodes.UNHANDLED_ERROR);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        return exception.GetBaseException() is SqlException
        {
            Number: 2601 or 2627
        };
    }

    private static ErrorResponse BuildUniqueConstraintError(DbUpdateException exception)
    {
        var databaseMessage = exception.GetBaseException().Message;

        if (databaseMessage.Contains("UX_ApplicationUsers_Dni", StringComparison.OrdinalIgnoreCase))
        {
            return CreateConflictError(nameof(ErrorCodes.PATIENT_DNI_CONFLICT), ErrorCodes.PATIENT_DNI_CONFLICT, "dni", "dni_already_registered");
        }

        if (databaseMessage.Contains("UX_Specialities_Name", StringComparison.OrdinalIgnoreCase))
        {
            return CreateConflictError(nameof(ErrorCodes.SPECIALITY_NAME_CONFLICT), ErrorCodes.SPECIALITY_NAME_CONFLICT, "name", "speciality_name_already_exists");
        }

        if (databaseMessage.Contains("UX_AvailabilitySlots_DoctorId_Start", StringComparison.OrdinalIgnoreCase))
        {
            return CreateConflictError(nameof(ErrorCodes.AVAILABILITY_SLOT_CONFLICT), ErrorCodes.AVAILABILITY_SLOT_CONFLICT, "start", "doctor_slot_already_exists");
        }

        if (databaseMessage.Contains("UX_Appointments_AvailabilitySlotId_Booked", StringComparison.OrdinalIgnoreCase))
        {
            return CreateConflictError(nameof(ErrorCodes.APPOINTMENT_SLOT_CONFLICT), ErrorCodes.APPOINTMENT_SLOT_CONFLICT, "availabilitySlotId", "slot_already_booked");
        }

        return new ErrorResponse(nameof(ErrorCodes.DATA_INTEGRITY_CONFLICT), ErrorCodes.DATA_INTEGRITY_CONFLICT);
    }

    private static ErrorResponse CreateConflictError(string errorCode,string message,string field,string issue)
    {
        var error = new ErrorResponse(errorCode, message);

        error.AddDetail(field, issue);

        return error;
    }
}