using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;
using Dsw2026Tpi.CrossCutting.Helpers;

namespace Dsw2026Tpi.Application.Services;

public class DoctorService : IDoctorService
{
    private readonly IPersistence _persistence;
    private const string DoctorNotFound = "Doctor";
    private const string SpecialityNotFound = "SpecialityId";

    public DoctorService(IPersistence persistence)
    {
        _persistence = persistence;
    }
    public async Task<Pagination<DoctorModel.Response>> GetAll(int pageSize, int pageIndex, string? name = null, Guid? specialtyId = null)
    {
        var doctors = await _persistence.Paginate<Doctor, string>(pageSize, pageIndex, d => !d.Deleted && d.IsActive && (string.IsNullOrWhiteSpace(name) || d.Name.Contains(name)) && (!specialtyId.HasValue || d.SpecialityId == specialtyId.Value), d => d.Name, nameof(Doctor.Speciality));
        return doctors.Map(MapResponse);
    }
    public async Task<DoctorModel.Response> Create(DoctorModel.Request request)
    {
        var speciality = await _persistence.GetById<Speciality>(request.SpecialtyId);

        if (speciality is null || speciality.Deleted)
            throw new EntityNotFoundException(SpecialityNotFound);

        var doctor = new Doctor( request.Name, request.LicenseNumber, speciality);
        await _persistence.Add(doctor);

        return MapResponse(doctor);
    }
    public async Task<DoctorModel.Response> Update(Guid id, DoctorModel.Request request)
    {
        var doctor = await _persistence.GetById<Doctor>(id);

        if (doctor is null || doctor.Deleted)
            throw new EntityNotFoundException(DoctorNotFound);

        var speciality = await _persistence.GetById<Speciality>(request.SpecialtyId);

        if (speciality is null || speciality.Deleted)
            throw new EntityNotFoundException(SpecialityNotFound);

        doctor.UpdateDetails(request.Name, request.LicenseNumber, speciality);
        await _persistence.Update(doctor);

        return MapResponse(doctor);
    }
    public async Task<IEnumerable<DoctorModel.AvailabilityResponse>> GetAvailabilities(Guid id)
    {
        var doctor = await _persistence.GetById<Doctor>(id);

        if (doctor is null || doctor.Deleted || !doctor.IsActive)
            throw new EntityNotFoundException(DoctorNotFound);

        var now = Clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // La disponibilidad es mensual: solo las reglas vigentes cargadas en el mes actual.
        var rules = await _persistence.GetFiltered<AvailabilityRule>(rule =>
            rule.DoctorId == id &&
            rule.IsActive &&
            rule.EffectiveFrom >= monthStart);

        if (rules is null)
            return Enumerable.Empty<DoctorModel.AvailabilityResponse>();

        return rules
            .SelectMany(rule => (rule.DaysOfWeekCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(day => day.Trim().ToUpperInvariant())
                .Select(day => new
                {
                    Order = Array.IndexOf(DayOrder, day),
                    Response = new DoctorModel.AvailabilityResponse(
                        rule.Id,
                        DayLabels.GetValueOrDefault(day, day),
                        rule.StartTime.ToString(@"hh\:mm"),
                        rule.EndTime.ToString(@"hh\:mm"))
                }))
            .OrderBy(x => x.Order)
            .ThenBy(x => x.Response.StartTime)
            .Select(x => x.Response)
            .ToList();
    }
    public async Task<IEnumerable<DoctorModel.SlotResponse>> GetSlots(Guid id, DateOnly fromDate, DateOnly toDate)
    {
        var doctor = await _persistence.GetById<Doctor>(id);

        if (doctor is null || doctor.Deleted || !doctor.IsActive)
            throw new EntityNotFoundException(DoctorNotFound);

        var rangeStart = fromDate.ToDateTime(TimeOnly.MinValue);
        var rangeEnd = toDate.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var now = Clock.Now;

        var slots = await _persistence.GetFiltered<AvailabilitySlot>(slot =>
            slot.DoctorId == id &&
            slot.Status == SlotStatus.AVAILABLE &&
            slot.Start > now &&
            slot.Start >= rangeStart &&
            slot.Start < rangeEnd);

        return (slots ?? Enumerable.Empty<AvailabilitySlot>())
            .OrderBy(slot => slot.Start)
            .Select(slot => new DoctorModel.SlotResponse(
                slot.Id,
                DateOnly.FromDateTime(slot.Start).ToString("yyyy-MM-dd"),
                slot.Start.ToString("HH:mm"),
                slot.End.ToString("HH:mm")))
            .ToList();
    }
    public async Task Delete(Guid id)
    {
        var doctor = await _persistence.GetById<Doctor>(id);
        if (doctor is null || doctor.Deleted)
            throw new EntityNotFoundException(DoctorNotFound);

        doctor.MarkAsDeleted();
        await _persistence.Update(doctor);
    }
    private static readonly string[] DayOrder =
    ["LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES", "SABADO", "DOMINGO"];
    private static readonly Dictionary<string, string> DayLabels = new()
    {
        ["LUNES"] = "LUNES",
        ["MARTES"] = "MARTES",
        ["MIERCOLES"] = "MIÉRCOLES",
        ["JUEVES"] = "JUEVES",
        ["VIERNES"] = "VIERNES",
        ["SABADO"] = "SÁBADO",
        ["DOMINGO"] = "DOMINGO"
    };
    private static DoctorModel.Response MapResponse(Doctor doctor)
    {
        return new DoctorModel.Response(doctor.Id, doctor.Name, doctor.LicenseNumber, new DoctorModel.SpecialtyDto(doctor.SpecialityId, doctor.Speciality?.Name ?? string.Empty));
    }
}