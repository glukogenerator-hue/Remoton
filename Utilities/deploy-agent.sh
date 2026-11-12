#!/usr/bin/env bash
#
# Выкладка пакетов агента на сервер Remoton.
#
# Скрипт запускается НА сервере, там где docker и контейнер с сервером. Пакеты к
# этому моменту уже скопированы в каталог staging, раскладка такая:
#
#   <staging>/x64/Remotely-Win-x64.zip
#   <staging>/x64/Remotely_Desktop.exe
#   <staging>/x86/Remotely-Win-x86.zip
#   <staging>/x86/Remotely_Desktop.exe
#
# Пример:
#   ./deploy-agent.sh --version 2026.11.12.0012 --staging /root/remoton-build/releases/2026.11.12.0012
#
# По умолчанию контейнер НЕ перезапускается: файлы копируются в работающий
# контейнер, а образ собирается новым слоем поверх текущего, чтобы выложенное
# пережило пересоздание контейнера. С --restart контейнер пересоздаётся сразу.
#
# Переменные окружения (значения по умолчанию — боевые):
#   REMOTON_APP_DIR     каталог с docker-compose.yml   (/docker_prod/remotly)
#   REMOTON_DB          файл базы                      (/var/www/remotely/Remotely.db)
#   REMOTON_CONTAINER   имя контейнера                 (remotely)
#   REMOTON_IMAGE       имя образа                     (remoton/server)
#   REMOTON_BACKUP_DIR  куда класть бэкапы             (/root/backups/remotely-deploys)
#   REMOTON_LOCAL_URL   адрес сервера на хосте         (http://127.0.0.1:5000)

set -euo pipefail

APP_DIR="${REMOTON_APP_DIR:-/docker_prod/remotly}"
DB="${REMOTON_DB:-/var/www/remotely/Remotely.db}"
CONTAINER="${REMOTON_CONTAINER:-remotely}"
IMAGE="${REMOTON_IMAGE:-remoton/server}"
BACKUP_DIR="${REMOTON_BACKUP_DIR:-/root/backups/remotely-deploys}"
LOCAL_URL="${REMOTON_LOCAL_URL:-http://127.0.0.1:5000}"

VERSION=""
STAGING=""
PLATFORMS="both"
IMAGE_TAG=""
RESTART=0
DRY_RUN=0

usage() {
    sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'
}

die() {
    echo "ОШИБКА: $*" >&2
    exit 1
}

log() {
    echo
    echo "== $*"
}

etag_of() {
    curl -sI "$LOCAL_URL/Content/Remotely-Win-$1.zip" | grep -i '^etag' | tr -d '\r' || true
}

health_of() {
    docker inspect -f '{{.State.Status}}/{{.State.Health.Status}}' "$CONTAINER" 2>/dev/null || echo "нет контейнера"
}

while [ $# -gt 0 ]; do
    case "$1" in
        --version) VERSION="${2:-}"; shift 2 ;;
        --staging) STAGING="${2:-}"; shift 2 ;;
        --platform) PLATFORMS="${2:-}"; shift 2 ;;
        --image-tag) IMAGE_TAG="${2:-}"; shift 2 ;;
        --restart) RESTART=1; shift ;;
        --dry-run) DRY_RUN=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) die "неизвестный аргумент: $1 (см. --help)" ;;
    esac
done

# --- Проверки аргументов -----------------------------------------------------

[ -n "$VERSION" ] || die "не указан --version"
echo "$VERSION" | grep -Eq '^[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]{4}$' \
    || die "версия должна быть в виде yyyy.MM.dd.HHmm, получено: $VERSION"
[ -n "$STAGING" ] || die "не указан --staging"
[ -d "$STAGING" ] || die "каталог staging не найден: $STAGING"

# Тег образа по умолчанию — версия без точек: 2026.11.12.0012 -> 202611120012.
# Так два выпуска в один день не затирают тег друг друга.
[ -n "$IMAGE_TAG" ] || IMAGE_TAG="$(echo "$VERSION" | tr -d '.')"

case "$PLATFORMS" in
    both) ARCHES=(x64 x86) ;;
    x64) ARCHES=(x64) ;;
    x86) ARCHES=(x86) ;;
    *) die "--platform принимает both, x64 или x86" ;;
esac

if [ ! -f "$APP_DIR/docker-compose.yml" ]; then
    if [ "$DRY_RUN" = 1 ]; then
        echo "предупреждение: нет $APP_DIR/docker-compose.yml (задайте REMOTON_APP_DIR)"
    else
        die "нет $APP_DIR/docker-compose.yml (задайте REMOTON_APP_DIR)"
    fi
fi

for arch in "${ARCHES[@]}"; do
    [ -f "$STAGING/$arch/Remotely-Win-$arch.zip" ] || die "нет $STAGING/$arch/Remotely-Win-$arch.zip"
    [ -f "$STAGING/$arch/Remotely_Desktop.exe" ] || die "нет $STAGING/$arch/Remotely_Desktop.exe"
done

for bin in docker curl sqlite3; do
    if ! command -v "$bin" >/dev/null; then
        if [ "$DRY_RUN" = 1 ]; then
            echo "предупреждение: не найден $bin (для проверочного прогона не нужен)"
        else
            die "нужен $bin"
        fi
    fi
done

log "Параметры"
echo "версия:        $VERSION"
echo "staging:       $STAGING"
echo "платформы:     ${ARCHES[*]}"
echo "тег образа:    $IMAGE:$IMAGE_TAG"
[ "$RESTART" = 1 ] && echo "режим:         с перезапуском контейнера" \
                   || echo "режим:         без перезапуска (файлы копируются в контейнер)"

if [ "$DRY_RUN" = 1 ]; then
    echo "режим:         проверочный прогон, ничего не меняется"
    for arch in "${ARCHES[@]}"; do
        echo "файлы $arch:   $(stat -Lc '%s' "$STAGING/$arch/Remotely-Win-$arch.zip") и $(stat -Lc '%s' "$STAGING/$arch/Remotely_Desktop.exe") байт"
    done
    log "Проверочный прогон: всё на месте, команды не выполнялись"
    exit 0
fi

CURRENT_IMAGE="$(docker inspect -f '{{.Config.Image}}' "$CONTAINER")" \
    || die "контейнер $CONTAINER не найден"

echo "текущий образ: $CURRENT_IMAGE"
echo "контейнер:     $CONTAINER ($(health_of))"

# --- Бэкап -------------------------------------------------------------------

mkdir -p "$BACKUP_DIR"
log "Бэкап базы"
BACKUP_FILE="$BACKUP_DIR/Remotely-before-$VERSION.db"
sqlite3 "$DB" ".timeout 30000" ".backup $BACKUP_FILE"
echo "база:      $BACKUP_FILE"
echo "целостность: $(sqlite3 "$BACKUP_FILE" 'PRAGMA integrity_check;')"

DEVICES_BEFORE="$(sqlite3 -readonly "$DB" 'select count(*) from Devices')"
echo "устройств: $DEVICES_BEFORE"

for arch in "${ARCHES[@]}"; do
    echo "ETag $arch до: $(etag_of "$arch")"
done

# --- Образ -------------------------------------------------------------------

# Собираем в отдельном контексте: у платформ одинаковое имя клиента, и в одном
# каталоге они бы затёрли друг друга.
CONTEXT="$(mktemp -d)"
COMPOSE_BACKUP="$APP_DIR/docker-compose.yml.before-$IMAGE_TAG"
trap 'rm -rf "$CONTEXT"' EXIT

log "Слой образа $IMAGE:$IMAGE_TAG"
{
    echo "FROM $CURRENT_IMAGE"
    for arch in "${ARCHES[@]}"; do
        # readlink -f: в staging могут лежать симлинки, а в контекст нужны сами файлы.
        cp "$(readlink -f "$STAGING/$arch/Remotely-Win-$arch.zip")" "$CONTEXT/Remotely-Win-$arch.zip"
        cp "$(readlink -f "$STAGING/$arch/Remotely_Desktop.exe")" "$CONTEXT/Remotely_Desktop-$arch.exe"
        echo "COPY Remotely-Win-$arch.zip /app/wwwroot/Content/Remotely-Win-$arch.zip"
        echo "COPY Remotely_Desktop-$arch.exe /app/wwwroot/Content/Win-$arch/Remotely_Desktop.exe"
    done
} > "$CONTEXT/Dockerfile"

cat "$CONTEXT/Dockerfile"
nice -n 10 docker build -f "$CONTEXT/Dockerfile" -t "$IMAGE:$IMAGE_TAG" "$CONTEXT" >/dev/null
echo "образ собран: $IMAGE:$IMAGE_TAG"

# --- Compose и применение ----------------------------------------------------

log "docker-compose.yml"
cp -a "$APP_DIR/docker-compose.yml" "$COMPOSE_BACKUP"
sed -i "s#image: .*#image: $IMAGE:$IMAGE_TAG#" "$APP_DIR/docker-compose.yml"
grep -n "image:" "$APP_DIR/docker-compose.yml" | head -3
echo "бэкап: $COMPOSE_BACKUP"

if [ "$RESTART" = 1 ]; then
    log "Перезапуск контейнера"
    (cd "$APP_DIR" && docker compose up -d)

    ok=0
    for _ in $(seq 1 45); do
        [ "$(health_of)" = "running/healthy" ] && ok=1 && break
        sleep 2
    done
    echo "состояние: $(health_of)"

    if [ "$ok" != 1 ]; then
        echo "!! контейнер не поднялся, откатываю образ"
        docker logs --tail 20 "$CONTAINER" 2>&1 | cut -c1-200
        cp -a "$COMPOSE_BACKUP" "$APP_DIR/docker-compose.yml"
        (cd "$APP_DIR" && docker compose up -d) || true
        sleep 10
        echo "после отката: $(health_of), образ $(docker inspect -f '{{.Config.Image}}' "$CONTAINER")"
        exit 1
    fi
else
    log "Копирование в работающий контейнер"
    for arch in "${ARCHES[@]}"; do
        # readlink -f: docker cp копирует симлинк как симлинк, а через симлинк в
        # контейнере ходит по ссылке и промахивается. Берём сами файлы.
        zip_src="$(readlink -f "$STAGING/$arch/Remotely-Win-$arch.zip")"
        exe_src="$(readlink -f "$STAGING/$arch/Remotely_Desktop.exe")"
        zip_size="$(stat -c '%s' "$zip_src")"
        exe_size="$(stat -c '%s' "$exe_src")"

        # Старые ссылки удаляем: docker cp пишет сквозь симлинк в цель.
        docker exec "$CONTAINER" rm -f \
            "/app/wwwroot/Content/Remotely-Win-$arch.zip" \
            "/app/wwwroot/Content/Win-$arch/Remotely_Desktop.exe"

        docker cp "$zip_src" "$CONTAINER:/app/wwwroot/Content/Remotely-Win-$arch.zip"
        docker cp "$exe_src" "$CONTAINER:/app/wwwroot/Content/Win-$arch/Remotely_Desktop.exe"

        in_zip="$(docker exec "$CONTAINER" stat -c '%s' "/app/wwwroot/Content/Remotely-Win-$arch.zip")"
        in_exe="$(docker exec "$CONTAINER" stat -c '%s' "/app/wwwroot/Content/Win-$arch/Remotely_Desktop.exe")"
        echo "$arch: пакет $in_zip байт, клиент $in_exe байт (ожидалось $zip_size и $exe_size)"
        if [ "$in_zip" != "$zip_size" ] || [ "$in_exe" != "$exe_size" ]; then
            die "размеры в контейнере не совпали — файлы скопированы неверно"
        fi
    done
    echo "состояние: $(health_of) (контейнер не перезапускался)"
fi

# --- Проверки ----------------------------------------------------------------

log "Проверки"
FAILED=0
for arch in "${ARCHES[@]}"; do
    expected="$(stat -c '%s' "$(readlink -f "$STAGING/$arch/Remotely-Win-$arch.zip")")"
    headers="$(curl -sI "$LOCAL_URL/Content/Remotely-Win-$arch.zip" | tr -d '\r')"
    served="$(echo "$headers" | grep -i '^content-length' | awk '{print $2}')"
    etag="$(echo "$headers" | grep -i '^etag' | cut -d' ' -f2-)"
    echo "$arch: отдаётся $served байт (ожидалось $expected), ETag $etag"
    if [ "$served" != "$expected" ]; then
        echo "  !! сервер отдаёт не тот файл"
        FAILED=1
    fi
done

echo "форма входа: $(curl -s -o /dev/null -w '%{http_code}' "$LOCAL_URL/Account/Login")"
echo "ключи защиты: $(ls "$(dirname "$DB")/DataProtection-Keys" 2>/dev/null | wc -l)"
DEVICES_AFTER="$(sqlite3 -readonly "$DB" 'select count(*) from Devices')"
echo "устройств: $DEVICES_BEFORE -> $DEVICES_AFTER"
[ "$DEVICES_BEFORE" = "$DEVICES_AFTER" ] || { echo "  !! число устройств изменилось"; FAILED=1; }

echo
echo "Онлайн-агенты по версиям:"
sqlite3 -readonly "$DB" \
    'select AgentVersion, count(*) from Devices where IsOnline = 1 group by AgentVersion order by 2 desc' \
    | sed 's/^/  /'

if [ "$FAILED" != 0 ]; then
    echo
    echo "ИТОГ: выложено с замечаниями, смотрите пометки !! выше"
    exit 1
fi

echo
echo "ИТОГ: выложено $IMAGE:$IMAGE_TAG (${ARCHES[*]})"
[ "$RESTART" = 1 ] || echo "Контейнер не перезапускался; образ обновлён, изменения переживут пересоздание."
