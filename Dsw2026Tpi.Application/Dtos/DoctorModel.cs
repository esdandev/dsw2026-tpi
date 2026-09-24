namespace Dsw2026Tpi.Application.Dtos;

public record DoctorModel
{
    public record Request(string Name, string LicenseNumber, Guid SpecialtyId);
    public record Response(Guid Id, string Name, string LicenseNumber, SpecialtyDto? Specialty);
    public record SpecialtyDto(Guid Id, string Name);
    public record AvailabilityResponse(Guid Id, string Day, string StartTime, string EndTime);
    public record SlotResponse(Guid Id, string Date, string StartTime, string EndTime);
    public record GetAllQuery(int PageSize, int PageIndex, string? Name, Guid? SpecialtyId = null);
}