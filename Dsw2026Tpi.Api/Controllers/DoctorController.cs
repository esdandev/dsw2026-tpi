using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Identity;
using FluentValidation;
using ValidationException = Dsw2026Tpi.CrossCutting.Exceptions.ValidationException;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2026Tpi.Api.Controllers;

[Route("api/doctors")]
[Authorize]
public class DoctorController : AppController
{
    private readonly IDoctorService _service;
    private readonly IValidator<DoctorModel.Request> _requestValidator;
    private readonly IValidator<DoctorModel.GetAllQuery> _getAllValidator;

    public DoctorController(IDoctorService service, IValidator<DoctorModel.Request> requestValidator,   IValidator<DoctorModel.GetAllQuery> getAllValidator)
    {
        _service = service;
        _requestValidator = requestValidator;
        _getAllValidator = getAllValidator;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll([FromQuery] int pageSize = 10, [FromQuery] int pageIndex = 0, [FromQuery] string? name = null, [FromQuery] Guid? specialtyId = null)
    {
        var query = new DoctorModel.GetAllQuery(pageSize, pageIndex, name, specialtyId);
        var validation = await _getAllValidator.ValidateAsync(query);
        Invalidez(validation);

        var doctors = await _service.GetAll(pageSize, pageIndex, name, specialtyId);
        return Ok(doctors);
    }

    [HttpGet("{id:guid}/availabilities")]
    [ProducesResponseType(typeof(IEnumerable<DoctorModel.AvailabilityResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAvailabilities(Guid id)
    {
        var availabilities = await _service.GetAvailabilities(id);
        return Ok(availabilities);
    }

    [HttpGet("{id:guid}/slots")]
    [ProducesResponseType(typeof(IEnumerable<DoctorModel.SlotResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSlots(Guid id, [FromQuery(Name = "from")] DateOnly? fromDate, [FromQuery(Name = "to")] DateOnly? toDate)
    {
        var errors = new ValidationException();

        if (fromDate is null)
            errors.WithDetail("from", "es obligatorio (YYYY-MM-DD)");
        if (toDate is null)
            errors.WithDetail("to", "es obligatorio (YYYY-MM-DD)");
        if (fromDate is not null && toDate is not null)
        {
            if (toDate < fromDate)
                errors.WithDetail("to", "no puede ser anterior a from");
            else if (toDate.Value.DayNumber - fromDate.Value.DayNumber > 31)
                errors.WithDetail("to", "el rango máximo es de 31 días");
        }

        if (errors.Error.Details.Count > 0)
            throw errors;

        var slots = await _service.GetSlots(id, fromDate!.Value, toDate!.Value);
        return Ok(slots);
    }

    [HttpPost]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(typeof(DoctorModel.Response), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] DoctorModel.Request request)
    {
        var validation = await _requestValidator.ValidateAsync(request);
        Invalidez(validation);

        var doctor = await _service.Create(request);
        return Created($"/api/doctors/{doctor.Id}", doctor);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(typeof(DoctorModel.Response), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(Guid id, [FromBody] DoctorModel.Request request)
    {
        var validation = await _requestValidator.ValidateAsync(request);
        Invalidez(validation);

        var doctor = await _service.Update(id, request);
        return Ok(doctor);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.AdminPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _service.Delete(id);
        return Ok("ok");
    }
}