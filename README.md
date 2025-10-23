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
Enviar XML crudo (como string) — ejemplo PowerShell
```powershell
$xml = '<?xml version="1.0" encoding="utf-8"?><plantilla>...</plantilla>'
Invoke-RestMethod -Method Post -Uri http://localhost:5005/api/publisher -Body (@{message=$xml} | ConvertTo-Json) -ContentType "application/json"
```

Enviar XML crudo — ejemplo curl
```bash
curl -X POST http://localhost:5005/api/publisher \
	-H "Content-Type: application/json" \
	-d '{"message":"<?xml version=\"1.0\" encoding=\"utf-8\"?><plantilla>...</plantilla>"}'
```

Enviar un objeto JSON en `message` (el servicio publicará JSON)
```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5005/api/publisher -Body (@{message=@{idPlantilla="plant1"; texto="<b>hola</b>"}} | ConvertTo-Json -Depth 5) -ContentType "application/json"
```

Enviar un objeto vacío `{}` en `message` — ejemplo PowerShell
```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5005/api/publisher -Body (@{message=@{}} | ConvertTo-Json -Depth 5) -ContentType "application/json"
```

Enviar un objeto vacío `{}` en `message` — ejemplo curl
```bash
curl -X POST http://localhost:5005/api/publisher \
	-H "Content-Type: application/json" \
	-d '{"message":{}}'
```

Comportamiento importante
- Si `message` es un string (por ejemplo contiene XML), el servicio publica los bytes UTF-8 crudos de esa cadena en NATS — es decir, el XML llega sin escapes.
- Si `message` es un objeto JSON, el servicio serializa el objeto y publica JSON. La serialización utiliza `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, por lo que caracteres como `<` no se convertirán en `\u003C`.

Nota: para comprobar la recepción en NATS puedes ejecutar un subscriber (ej. usando `nats` CLI o el contenedor Docker oficial) y observar el payload recibido.

Notas
- La configuración de Kestrel (puerto) está en `appsettings.json`.
- Si quieres HTTPS en desarrollo, genera el certificado dev: `dotnet dev-certs https --trust`.
