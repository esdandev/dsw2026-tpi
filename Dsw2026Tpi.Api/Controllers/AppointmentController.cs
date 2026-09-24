using Dsw2026Tpi.Api.Configurations;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Domain.Entities;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Globalization;
using ValidationException = Dsw2026Tpi.CrossCutting.Exceptions.ValidationException;

namespace Dsw2026Tpi.Api.Controllers;

[Route("api/appointments")]
public class AppointmentController : AppController
{
    private readonly IAppointmentService _service;
    private readonly IValidator<AppointmentModel.CreateRequest> _createValidator;
    private readonly IValidator<AppointmentModel.GetByDateQuery> _getByDateValidator;
    private readonly IValidator<AppointmentModel.SearchQuery> _searchValidator;
    public AppointmentController(IAppointmentService service, IValidator<AppointmentModel.CreateRequest> createValidator, IValidator<AppointmentModel.GetByDateQuery> getByDateValidator, IValidator<AppointmentModel.SearchQuery> searchValidator)
    {
        _service = service;
        _createValidator = createValidator;
        _getByDateValidator = getByDateValidator;
        _searchValidator = searchValidator;
    }

    [HttpGet]
    [Authorize(Policy = Dsw2026Tpi.CrossCutting.Identity.Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetByDate([FromQuery] string date, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0, [FromQuery] string? status = null)
    {
        var query = new AppointmentModel.GetByDateQuery(date, pageSize, pageIndex);
        var validation = await _getByDateValidator.ValidateAsync(query);

        Invalidez(validation);

        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            throw new ValidationException()
                .WithDetail( "date", "formato inválido, se requiere YYYY-MM-DD");
        }

        var appointments = await _service.GetByDate(parsedDate, pageSize, pageIndex, ParseStatus(status));
        return Ok(appointments);
    }

    [HttpGet("search")]
    [Authorize(Policy = Dsw2026Tpi.CrossCutting.Identity.Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Search([FromQuery] Guid? specialtyId, [FromQuery] Guid? doctorId, [FromQuery] long? dni, [FromQuery] string? date, [FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0, [FromQuery] string? status = null)
    {
        var query = new AppointmentModel.SearchQuery(specialtyId, doctorId, dni, date, pageSize, pageIndex);
        var validation = await _searchValidator.ValidateAsync(query);
        Invalidez(validation);

        DateOnly? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                throw new ValidationException().WithDetail("date", "formato inválido, se requiere YYYY-MM-DD");
            parsedDate = d;
        }

        var result = await _service.Search(specialtyId, doctorId, dni, parsedDate, pageSize, pageIndex, ParseStatus(status));
        return Ok(result);
    }

    [HttpGet("patient")]
    [Authorize(Policy = Dsw2026Tpi.CrossCutting.Identity.Policies.PatientPolicy)]
    [ProducesResponseType(typeof(IEnumerable<AppointmentModel.PatientResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByPatient([FromQuery] long dni)
    {
        var authenticatedUserName = GetAuthenticatedUserName();

        var appointments = await _service.GetByPatient(dni, authenticatedUserName);
        return Ok(appointments);
    }

    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AppointmentCreate)]
    [Authorize(Policy = Dsw2026Tpi.CrossCutting.Identity.Policies.PatientPolicy)]
    [ProducesResponseType(typeof(AppointmentModel.CreateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]

    public async Task<IActionResult> Create([FromBody] AppointmentModel.CreateRequest request)
    {
        var authenticatedUserName = GetAuthenticatedUserName();

        var validation = await _createValidator.ValidateAsync(request);
        Invalidez(validation);

        var appointment = await _service.Create(request, authenticatedUserName);
        return Created($"/api/appointments/{appointment.Id}", appointment);
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = Dsw2026Tpi.CrossCutting.Identity.Policies.PatientPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var authenticatedUserName = GetAuthenticatedUserName();

        await _service.Cancel(id, authenticatedUserName);
        return Ok("ok");
    }

    private static AppointmentStatus? ParseStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        if (int.TryParse(status, out _) ||
            !Enum.TryParse<AppointmentStatus>(status.Trim(), true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new ValidationException()
                .WithDetail("status", "valor inválido, use BOOKED, CANCELLED, ATTENDED o NO_SHOW");
        }

        return parsed;
    }

    private string GetAuthenticatedUserName()
    {
        var authenticatedUserName = User.Identity?.Name;

        if (string.IsNullOrWhiteSpace(authenticatedUserName))
            throw new AuthenticationException();

        return authenticatedUserName;
    }
}
