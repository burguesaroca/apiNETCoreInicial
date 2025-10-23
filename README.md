# MicroservicioDemo

Microservicio minimal en ASP.NET Core que expone un endpoint POST `/api/publisher`.

Requisitos
- .NET SDK instalado (recomendado .NET 8 o 7 según tu SDK).

Restaurar y compilar
```powershell
dotnet restore
dotnet build
```

Ejecutar
```powershell
# Ejecuta la app (usa la configuración de appsettings.json para el puerto)
dotnet run --project C:\apiNETCoreInicial\MicroservicioDemo.csproj
```

Probar el endpoint (PowerShell)
```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5005/api/publisher -Body (@{message="testing..."} | ConvertTo-Json) -ContentType "application/json"
```

Notas
- La configuración de Kestrel (puerto) está en `appsettings.json`.
- Si quieres HTTPS en desarrollo, genera el certificado dev: `dotnet dev-certs https --trust`.
