using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IDoctorService
{
    Task<Pagination<DoctorModel.Response>> GetAll(int pageSize, int pageIndex, string? name = null, Guid? specialtyId = null);
    Task<DoctorModel.Response> Create(DoctorModel.Request request);
    Task<DoctorModel.Response> Update( Guid id, DoctorModel.Request request);
    Task<IEnumerable<DoctorModel.AvailabilityResponse>> GetAvailabilities(Guid id);
    Task<IEnumerable<DoctorModel.SlotResponse>> GetSlots(Guid id, DateOnly fromDate, DateOnly toDate);
    Task Delete(Guid id);
}