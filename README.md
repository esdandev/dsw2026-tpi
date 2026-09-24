# Trabajo Práctico Integrador
## Desarrollo de Software 2026
## Comisión 3k3
## Integrantes - Legajos

- Escalante Daniel Alejo Baltazar - 60554
- Lisandro Ruiz - 58399
- Mauro Benjamín Ibarra - 60698
- Gutierrez, Maia Federica - 60348

## Cómo ejecutar el proyecto localmente

1. Requisitos: .NET 9 SDK, SQL Server (o LocalDB) y un cliente de API (Swagger incluido).
2. Cloná el repositorio y pará en la carpeta raíz.
3. Configurá la cadena de conexión en `Dsw2026Tpi.Api/appsettings.Development.json`, clave `ConnectionStrings:DefaultConnection`.
4. Aplicá las migraciones:
   dotnet ef database update --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api --context Dsw2026TpiDbContext
   dotnet ef database update --project Dsw2026Tpi.Data --startup-project Dsw2026Tpi.Api --context AuthenticationDbContext
5. Corré la API:
   dotnet run --project Dsw2026Tpi.Api
6. Abrí `https://localhost:7075/swagger` (o el puerto que indique la consola).
7. Al iniciar por primera vez se crea un usuario administrador semilla: `admin@utn.com` / `Admin123!`.

## Endpoints implementados

### Autenticación (`/api/auth`)
- `POST /api/auth/admin/login` — login de administrador (email + password).
- `POST /api/auth/patient/login` — login de paciente (email + dni). Si el paciente no existe, se registra automáticamente.
- `POST /api/auth/admin/register` — registro de administradores (temporal, para pruebas).

### Especialidades (`/api/specialties`) — requiere token
- `GET /api/specialties` — listado paginado, filtrable por `name`.
- `POST /api/specialties` — alta (admin).
- `PUT /api/specialties/{id}` — modificación (admin).
- `DELETE /api/specialties/{id}` — baja lógica (admin).

### Médicos (`/api/doctors`) — requiere token
- `GET /api/doctors` — listado paginado, filtrable por `name` y `specialtyId`.
- `GET /api/doctors/{id}/availabilities` — reglas de disponibilidad del mes actual.
- `GET /api/doctors/{id}/slots?from=&to=` — turnos disponibles del médico en el rango de fechas.
- `POST /api/doctors` — alta (admin).
- `PUT /api/doctors/{id}` — modificación (admin).
- `DELETE /api/doctors/{id}` — baja lógica (admin).

### Disponibilidades (`/api/availabilities`) — admin
- `POST /api/availabilities` — carga la disponibilidad semanal de un médico y genera los turnos del mes.
- `PUT /api/availabilities` — reemplaza la disponibilidad vigente y regenera los turnos futuros no reservados.

### Turnos (`/api/appointments`)
- `POST /api/appointments` — reserva un turno (paciente).
- `DELETE /api/appointments/{id}` — cancela un turno propio (paciente).
- `GET /api/appointments/patient?dni=` — turnos activos y futuros del paciente autenticado.
- `GET /api/appointments?date=&status=` — turnos de un día (admin), filtro opcional por estado.
- `GET /api/appointments/search?...` — búsqueda combinada por especialidad, médico, dni, fecha y estado (admin).

## Notas
- Todos los endpoints (salvo login) requieren `Authorization: Bearer <token>`.
- Los errores siguen el formato `{ errorCode, message, details? }`.
