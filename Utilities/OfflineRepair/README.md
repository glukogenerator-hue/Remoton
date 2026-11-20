# Офлайн-восстановление агента Remoton

Для машин без доступа по сети/домену: копируется на флешку и запускается на месте.

## Что положить в эту папку

1. `Install-Remotely.ps1` — скачать с сервера:
   https://help.urldot.ru/Content/Install-Remotely.ps1
2. `Remotely-Win-x64.zip` — скачать с сервера:
   https://help.urldot.ru/Content/Remotely-Win-x64.zip
   (и `Remotely-Win-x86.zip`, если есть 32-битные машины)
3. `repair-remotely.bat` — из этого репозитория.

## Запуск на машине

Правый клик на `repair-remotely.bat` → «Запуск от имени администратора».

Скрипт:
- определяет разрядность и берёт нужный пакет;
- показывает текущее состояние (размер `deps.json`, статус службы);
- ставит/восстанавливает агент **из локального файла** (интернет не нужен);
- в конце печатает статус службы и версию агента.

DeviceID сохраняется, если на машине есть `ConnectionInfo.json` (в `C:\Program Files\Remotely`, в `Remotely.old` или в `%TEMP%\Remotely-ConnectionInfo.json`).

## Проверка на месте

```powershell
Get-Service Remotely_Service
(Get-Item "C:\Program Files\Remotely\Remotely_Agent.deps.json").Length
```

Норма: служба `Running`, размер `deps.json` около 126 000 байт (0 — признак прежней поломки).

## Если ConnectionInfo.json потерян

Машина появится в панели как новая запись (новый DeviceID). Старые «мёртвые» записи этого же имени можно удалить в интерфейсе после восстановления.
