# Remoton

Форк Remotely: удалённое управление и поддержка (.NET + веб-клиент).

## Структура
- `Server/` — ASP.NET сервер; веб-клиент в `Server/wwwroot/src` (TypeScript, скомпилированный `.js` лежит рядом и хранится в git)
- `Agent/` — агент; `Desktop.*` — удалённый рабочий стол (Win, Linux, UI, Native, Shared)
- `Shared/` — общие модели; `Tests/` — Server.Tests, Shared.Tests, LoadTester
- `docker-compose/` — запуск сервера в Docker

## Команды
- Сборка: `dotnet build Remotely.sln`
- Тесты: `dotnet test Remotely.sln`
- Форматирование: `dotnet format Remotely.sln --verify-no-changes`
- Docker: `docker compose -f docker-compose/docker-compose.yml build` / `up`

## Заметки
- Общение с пользователем — только на русском.
- При правке `wwwroot/src/*.ts` пересобирать соответствующие `.js` и `.js.map`.
