using System.Globalization;
using Dsw2026Tpi.Application.Dtos;
using Dsw2026Tpi.Application.Interfaces;
using Dsw2026Tpi.CrossCutting.Exceptions;
using Dsw2026Tpi.CrossCutting.Helpers;
using Dsw2026Tpi.Domain.Entities;
using Dsw2026Tpi.Domain.Interfaces;

namespace Dsw2026Tpi.Application.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly IPersistence _persistence;
    private readonly IHolidayProvider _holidayProvider;

    public AvailabilityService(IPersistence persistence, IHolidayProvider holidayProvider)
    {
        _persistence = persistence;
        _holidayProvider = holidayProvider;
    }

    private static readonly string[] ValidDayNames =
    {
        "LUNES", "MARTES", "MIERCOLES", "JUEVES", "VIERNES", "SABADO", "DOMINGO"
    };

    private static string NormalizeDayName(string day)
    {
        if (string.IsNullOrWhiteSpace(day))
            throw new ValidationException().WithDetail("days", "día inválido");

        var normalized = day.Trim()
            .ToUpperInvariant()
            .Replace("É", "E").Replace("Á", "A");

        if (!ValidDayNames.Contains(normalized))
            throw new ValidationException().WithDetail("days", $"día inválido: '{day}'. Debe ser LUNES, MARTES, MIERCOLES, JUEVES, VIERNES, SABADO o DOMINGO");

        return normalized;
    }
    private static void ValidateNoOverlapsWithinRequest(IReadOnlyCollection<(string Day, TimeSpan Start, TimeSpan End)> schedules)
    {
        var schedulesByDay = schedules.GroupBy(schedule => schedule.Day);

        foreach (var dayGroup in schedulesByDay)
        {
            var orderedSchedules = dayGroup
                .OrderBy(schedule => schedule.Start)
                .ThenBy(schedule => schedule.End)
                .ToList();

            for (var i = 1; i < orderedSchedules.Count; i++)
            {
                var previous = orderedSchedules[i - 1];
                var current = orderedSchedules[i];

                if (current.Start < previous.End)
                {
                    var previousRange = $"{previous.Start:hh\\:mm}-{previous.End:hh\\:mm}";

                    var currentRange = $"{current.Start:hh\\:mm}-{current.End:hh\\:mm}";

                    throw new ValidationException().WithDetail("days", $"Los horarios del día {dayGroup.Key} se solapan: " + $"{previousRange} y {currentRange}");
                }
            }
        }
    }
    private static string GetDayName(DayOfWeek dow) => dow switch
    {
        DayOfWeek.Monday => "LUNES",
        DayOfWeek.Tuesday => "MARTES",
        DayOfWeek.Wednesday => "MIERCOLES",
        DayOfWeek.Thursday => "JUEVES",
        DayOfWeek.Friday => "VIERNES",
        DayOfWeek.Saturday => "SABADO",
        DayOfWeek.Sunday => "DOMINGO",
        _ => throw new ArgumentOutOfRangeException(nameof(dow))
    };
    private async Task GenerateSlotsForRule(AvailabilityRule rule, DateTime fromDate, DateTime toDate)
    {
        var excluded = new HashSet<DateOnly>();
        if (!string.IsNullOrWhiteSpace(rule.ExcludedDatesCsv))
        {
            foreach (var s in rule.ExcludedDatesCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (DateOnly.TryParse(s, out var d)) excluded.Add(d);
            }
        }

        var daysOfWeek = rule.DaysOfWeekCsv?
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet() ?? new HashSet<string>();

        var current = fromDate.Date;
        var slotsToAdd = new List<AvailabilitySlot>();

        while (current.Date <= toDate.Date)
        {
            if (!daysOfWeek.Contains(GetDayName(current.DayOfWeek)))
            {
                current = current.AddDays(1);
                continue;
            }

            var dateOnly = DateOnly.FromDateTime(current);
            if (excluded.Contains(dateOnly) || _holidayProvider.IsNonWorkingDay(dateOnly))
            {
                current = current.AddDays(1);
                continue;
            }

            var startTime = rule.StartTime;
            var endTime = rule.EndTime;

            var slotStart = current.Add(startTime);
            var slotDuration = rule.SlotDuration;

            while (slotStart.Add(slotDuration) <= current.Add(endTime))
            {
                var slotEnd = slotStart.Add(slotDuration);
                var slot = new AvailabilitySlot
                {
                    Id = Guid.NewGuid(),
                    DoctorId = rule.DoctorId,
                    RuleId = rule.Id,
                    Start = DateTime.SpecifyKind(slotStart, DateTimeKind.Utc),
                    End = DateTime.SpecifyKind(slotEnd, DateTimeKind.Utc),
                    Status = SlotStatus.AVAILABLE,
                    Capacity = rule.Capacity,
                    BookedCount = 0
                };

                slotsToAdd.Add(slot);

                slotStart = slotStart.Add(slotDuration);
            }

            current = current.AddDays(1);
        }

        foreach (var s in slotsToAdd)
        {
            await _persistence.Add(s);
        }
    }
    public async Task<List<AvailabilityModel.Response>> Create(AvailabilityModel.Request request)
    {
        var doctorId = Guid.Parse(request.DoctorId);
        var doctor = await _persistence.GetById<Doctor>(doctorId);

        if (doctor is null || doctor.Deleted || !doctor.IsActive)
            throw new EntityNotFoundException("Doctor");

        if (request.Days == null || !request.Days.Any())
            throw new ValidationException().WithDetail("days", "se requiere al menos un dia con horario");

        var parsed = ParseAndValidateDays(request.Days);

        ValidateNoOverlapsWithinRequest(parsed);

        var currentMonthStart = new DateTime(Clock.Now.Year, Clock.Now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var existing = await _persistence.GetFiltered<AvailabilityRule>(r =>
            r.DoctorId == doctorId && r.IsActive && r.EffectiveFrom >= currentMonthStart);
        foreach (var (day, start, end) in parsed)
        {
            foreach (var er in existing ?? Enumerable.Empty<AvailabilityRule>())
            {
                var existingDays = er.DaysOfWeekCsv?
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim().ToUpperInvariant())
                    .ToHashSet() ?? new HashSet<string>();

                if (!existingDays.Contains(day)) continue;

                if (er.StartTime < end && er.EndTime > start)
                    throw new BusinessRuleException("AVAILABILITY_OVERLAP", "La regla de disponibilidad solapa con una regla existente.");
            }
        }

        var groups = parsed.GroupBy(p => (p.Start, p.End));
        var createdRules = new List<AvailabilityRule>();

        foreach (var group in groups)
        {
            var rule = new AvailabilityRule
            {
                Id = Guid.NewGuid(),
                DoctorId = doctorId,
                EffectiveFrom = Clock.Now,
                EffectiveTo = null,
                Recurrence = RecurrenceType.WEEKLY,
                DaysOfWeekCsv = string.Join(',', group.Select(g => g.Day)),
                StartTime = group.Key.Start,
                EndTime = group.Key.End,
                SlotDuration = TimeSpan.FromMinutes(30),
                Capacity = 1,
                IsActive = true,
                ExcludedDatesCsv = null
            };

            await _persistence.Add(rule);
            await GenerateSlotsForRule(rule, Clock.Today,
                new DateTime(Clock.Now.Year, Clock.Now.Month, DateTime.DaysInMonth(Clock.Now.Year, Clock.Now.Month)));

            createdRules.Add(rule);
        }

        return createdRules.Select(r => new AvailabilityModel.Response
        {
            Id = r.Id,
            DoctorId = r.DoctorId,
            EffectiveFrom = r.EffectiveFrom,
            EffectiveTo = r.EffectiveTo,
            Recurrence = r.Recurrence,
            DaysOfWeekCsv = r.DaysOfWeekCsv,
            StartTime = r.StartTime,
            EndTime = r.EndTime,
            SlotDuration = r.SlotDuration,
            Capacity = r.Capacity,
            IsActive = r.IsActive,
            ExcludedDatesCsv = r.ExcludedDatesCsv
        }).ToList();
    }
    public async Task<List<AvailabilityModel.Response>> Update(AvailabilityModel.Request request)
    {
        var doctorId = Guid.Parse(request.DoctorId);
        var doctor = await _persistence.GetById<Doctor>(doctorId);

        if (doctor is null || doctor.Deleted || !doctor.IsActive)
            throw new EntityNotFoundException("Doctor");

        if (request.Days == null || !request.Days.Any())
            throw new ValidationException().WithDetail("days", "se requiere al menos un dia con horario");

        var parsed = ParseAndValidateDays(request.Days);

        ValidateNoOverlapsWithinRequest(parsed);

        var now = Clock.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var nextMonthStart = monthStart.AddMonths(1);

        var slotsInMonth = await _persistence.GetFiltered<AvailabilitySlot>(slot =>
            slot.DoctorId == doctorId && slot.Start > now && slot.Start < nextMonthStart);

        var existingSlots = (slotsInMonth ?? Enumerable.Empty<AvailabilitySlot>()).ToList();

        var existingRules = await _persistence.GetFiltered<AvailabilityRule>(rule =>
            rule.DoctorId == doctorId &&
            rule.IsActive &&
            rule.EffectiveFrom < nextMonthStart &&
            (rule.EffectiveTo == null || rule.EffectiveTo >= monthStart));

        var referencedSlotIds = new HashSet<Guid>();
        var slotIds = existingSlots.Select(slot => slot.Id).ToList();

        if (slotIds.Count > 0)
        {
            var appointments = await _persistence.GetFiltered<Appointment>(appointment => slotIds.Contains(appointment.AvailabilitySlotId));

            if (appointments != null)
                referencedSlotIds = appointments.Select(appointment => appointment.AvailabilitySlotId).ToHashSet();
        }

        var existingSlotsByStart = existingSlots.ToDictionary(slot => slot.Start, slot => slot);
        var desiredSlotStarts = new HashSet<DateTime>();
        var createdRules = new List<AvailabilityRule>();
        var groups = parsed.GroupBy(schedule => (schedule.Start, schedule.End));

        foreach (var group in groups)
        {
            var rule = new AvailabilityRule
            {
                Id = Guid.NewGuid(),
                DoctorId = doctorId,
                EffectiveFrom = now,
                EffectiveTo = null,
                Recurrence = RecurrenceType.WEEKLY,
                DaysOfWeekCsv = string.Join(',', group.Select(schedule => schedule.Day)),
                StartTime = group.Key.Start,
                EndTime = group.Key.End,
                SlotDuration = TimeSpan.FromMinutes(30),
                Capacity = 1,
                IsActive = true,
                ExcludedDatesCsv = null
            };

            await _persistence.Add(rule);
            createdRules.Add(rule);

            var ruleDays = group.Select(schedule => schedule.Day).ToHashSet();
            var currentDate = now.Date;

            while (currentDate < nextMonthStart)
            {
                var dayName = GetDayName(currentDate.DayOfWeek);

                if (!ruleDays.Contains(dayName))
                {
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                var dateOnly = DateOnly.FromDateTime(currentDate);

                if (_holidayProvider.IsNonWorkingDay(dateOnly))
                {
                    currentDate = currentDate.AddDays(1);
                    continue;
                }

                var slotStart = DateTime.SpecifyKind(currentDate.Add(rule.StartTime), DateTimeKind.Utc);
                var dayEnd = DateTime.SpecifyKind(currentDate.Add(rule.EndTime), DateTimeKind.Utc);

                while (slotStart.Add(rule.SlotDuration) <= dayEnd)
                {
                    var slotEnd = slotStart.Add(rule.SlotDuration);

                    if (slotStart > now)
                    {
                        desiredSlotStarts.Add(slotStart);

                        if (existingSlotsByStart.TryGetValue(slotStart, out var existingSlot))
                        {
                            if (existingSlot.Status != SlotStatus.BOOKED)
                            {
                                existingSlot.RuleId = rule.Id;
                                existingSlot.End = slotEnd;
                                existingSlot.Status = SlotStatus.AVAILABLE;
                                existingSlot.Capacity = rule.Capacity;
                                existingSlot.BookedCount = 0;

                                await _persistence.Update(existingSlot);
                            }
                        }
                        else
                        {
                            var newSlot = new AvailabilitySlot
                            {
                                Id = Guid.NewGuid(),
                                DoctorId = doctorId,
                                RuleId = rule.Id,
                                Start = slotStart,
                                End = slotEnd,
                                Status = SlotStatus.AVAILABLE,
                                Capacity = rule.Capacity,
                                BookedCount = 0
                            };

                            await _persistence.Add(newSlot);
                        }
                    }

                    slotStart = slotEnd;
                }

                currentDate = currentDate.AddDays(1);
            }
        }

        foreach (var existingSlot in existingSlots)
        {
            if (desiredSlotStarts.Contains(existingSlot.Start))
                continue;

            if (existingSlot.Status == SlotStatus.BOOKED)
                continue;

            if (referencedSlotIds.Contains(existingSlot.Id))
            {
                existingSlot.Status = SlotStatus.LOCKED;
                await _persistence.Update(existingSlot);
                continue;
            }

            await _persistence.Delete(existingSlot);
        }

        foreach (var existingRule in existingRules ?? Enumerable.Empty<AvailabilityRule>())
        {
            existingRule.IsActive = false;
            existingRule.EffectiveTo = now;

            await _persistence.Update(existingRule);
        }

        return createdRules.Select(rule => new AvailabilityModel.Response
        {
            Id = rule.Id,
            DoctorId = rule.DoctorId,
            EffectiveFrom = rule.EffectiveFrom,
            EffectiveTo = rule.EffectiveTo,
            Recurrence = rule.Recurrence,
            DaysOfWeekCsv = rule.DaysOfWeekCsv,
            StartTime = rule.StartTime,
            EndTime = rule.EndTime,
            SlotDuration = rule.SlotDuration,
            Capacity = rule.Capacity,
            IsActive = rule.IsActive,
            ExcludedDatesCsv = rule.ExcludedDatesCsv
        }).ToList();
    }
    private static List<(string Day, TimeSpan Start, TimeSpan End)> ParseAndValidateDays(IEnumerable<AvailabilityModel.DayRequest> days)
    {
        var parsed = new List<(string Day, TimeSpan Start, TimeSpan End)>();

        foreach (var day in days)
        {
            var dayName = NormalizeDayName(day.Day);

            if (!TimeSpan.TryParseExact(day.StartTime, @"hh\:mm", CultureInfo.InvariantCulture, out var start))
                throw new ValidationException().WithDetail($"days[{day.Day}].startTime", "formato inválido, se requiere HH:mm");

            if (!TimeSpan.TryParseExact(day.EndTime, @"hh\:mm", CultureInfo.InvariantCulture, out var end))
                throw new ValidationException().WithDetail($"days[{day.Day}].endTime", "formato inválido, se requiere HH:mm");

            if (start >= end)
                throw new ValidationException().WithDetail($"days[{day.Day}]", "La hora de inicio debe ser antes de la hora de finalización");

            parsed.Add((dayName, start, end));
        }

        return parsed;
    }
}
