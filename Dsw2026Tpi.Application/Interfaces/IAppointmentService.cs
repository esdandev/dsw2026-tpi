﻿using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Domain.Entities;

namespace Dsw2026Tpi.Application.Interfaces;

public interface IAppointmentService
{
    Task<Pagination<AppointmentModel.AdministrativeResponse>> GetByDate(DateOnly date, int pageSize, int pageIndex, AppointmentStatus? status = null);
    Task<AppointmentModel.CreateResponse> Create(AppointmentModel.CreateRequest request, string authenticatedUserName);
    Task<Pagination<AppointmentModel.SearchAdministrativeResponse>> Search(Guid? specialtyId, Guid? doctorId, long? dni, DateOnly? date, int pageSize, int pageIndex, AppointmentStatus? status = null);
    Task Cancel(Guid id, string authenticatedUserName);
    Task<IEnumerable<AppointmentModel.PatientResponse>> GetByPatient(long dni, string authenticatedUserName);
}