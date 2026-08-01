#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
item_id="${WORKSHOP_ITEM_ID:-3774648307}"
appid="${WORKSHOP_APP_ID:-244850}"
steamcmd="${STEAMCMD:-steamcmd}"
title="${WORKSHOP_TITLE:-RSS Orbital Navigator}"
staging_dir="${WORKSHOP_STAGING_DIR:-${repo_root}/.bootstrap/workshop-content}"
vdf_file="${WORKSHOP_VDF_FILE:-${repo_root}/.bootstrap/workshop-item.vdf}"
dry_run=false

usage() {
    printf 'Usage: %s [--dry-run]\n' "${BASH_SOURCE[0]}"
    printf '\nEnvironment overrides:\n'
    printf '  STEAMCMD             SteamCMD executable or Windows path\n'
    printf '  STEAM_USERNAME       Steam account name\n'
    printf '  WORKSHOP_ITEM_ID     Existing Workshop item ID (default: %s)\n' "$item_id"
    printf '  WORKSHOP_STAGING_DIR Temporary content directory\n'
}

while (($# > 0)); do
    case "$1" in
        --dry-run)
            dry_run=true
            ;;
        --help|-h)
            usage
            exit 0
            ;;
        *)
            printf 'Unknown argument: %s\n' "$1" >&2
            usage >&2
            exit 2
            ;;
    esac
    shift
done

if [[ ! -d "${repo_root}/Data" ]]; then
    printf 'Data directory not found: %s\n' "${repo_root}/Data" >&2
    exit 1
fi
if [[ ! -f "${repo_root}/SteamWorkshopDescription.txt" ]]; then
    printf 'Workshop description not found.\n' >&2
    exit 1
fi
if [[ ! "${item_id}" =~ ^[0-9]+$ ]]; then
    printf 'WORKSHOP_ITEM_ID must contain only digits.\n' >&2
    exit 1
fi

rm -rf "${staging_dir}"
mkdir -p "${staging_dir}" "$(dirname "${vdf_file}")"
cp -R "${repo_root}/Data" "${staging_dir}/"

description="$(<"${repo_root}/SteamWorkshopDescription.txt")"
description="${description//\\/\\\\}"
description="${description//\"/\\\"}"
cat > "${vdf_file}" <<EOF
"workshopitem"
{
    "appid" "${appid}"
    "publishedfileid" "${item_id}"
    "contentfolder" "${staging_dir}"
    "title" "${title}"
    "description" "${description}"
    "changenote" "Updated from repository release $(sed -n 's/^# RSS Orbital Navigator //p' "${repo_root}/README.md" | head -n 1)"
}
EOF

printf 'Workshop item: %s\n' "${item_id}"
printf 'Content: %s\n' "${staging_dir}"
printf 'VDF: %s\n' "${vdf_file}"

if [[ "${dry_run}" == true ]]; then
    printf '\nDry run only. Generated VDF:\n\n'
    printf '%s\n' "$(<"${vdf_file}")"
    exit 0
fi

if ! command -v "${steamcmd}" >/dev/null 2>&1 && [[ ! -x "${steamcmd}" ]]; then
    printf 'SteamCMD not found: %s\n' "${steamcmd}" >&2
    printf 'Set STEAMCMD to the executable path and retry.\n' >&2
    exit 1
fi
if [[ -z "${STEAM_USERNAME:-}" ]]; then
    read -r -p 'Steam username: ' STEAM_USERNAME
fi
if [[ -z "${STEAM_USERNAME}" ]]; then
    printf 'Steam username is required.\n' >&2
    exit 1
fi
read -r -p 'Publish/update Workshop item now? [y/N] ' confirmation
if [[ ! "${confirmation}" =~ ^[Yy]$ ]]; then
    printf 'Cancelled.\n'
    exit 0
fi

steamcmd_args=(+login "${STEAM_USERNAME}")
steamcmd_args+=(+workshop_build_item "${vdf_file}" +quit)
"${steamcmd}" "${steamcmd_args[@]}"
