#!/usr/bin/env bash
# Usage: cert-expiry.sh [dir ...]   (defaults to current directory)
# Prints expiration date for every certificate found under the given dirs.

set -u

dirs=("$@")
[[ ${#dirs[@]} -eq 0 ]] && dirs=(.)

now_epoch=$(date -u +%s)
expired=()

print_cert() {
    local file=$1 fmt=$2
    local end_date end_epoch days subject

    case $fmt in
        pem)
            end_date=$(openssl x509 -in "$file" -noout -enddate 2>/dev/null | cut -d= -f2)
            subject=$(openssl x509 -in "$file" -noout -subject 2>/dev/null | sed 's/^subject= *//')
            ;;
        der)
            end_date=$(openssl x509 -in "$file" -inform DER -noout -enddate 2>/dev/null | cut -d= -f2)
            subject=$(openssl x509 -in "$file" -inform DER -noout -subject 2>/dev/null | sed 's/^subject= *//')
            ;;
        p12)
            end_date=$(openssl pkcs12 -in "$file" -nokeys -passin pass: 2>/dev/null \
                | openssl x509 -noout -enddate 2>/dev/null | cut -d= -f2)
            subject=$(openssl pkcs12 -in "$file" -nokeys -passin pass: 2>/dev/null \
                | openssl x509 -noout -subject 2>/dev/null | sed 's/^subject= *//')
            ;;
    esac

    if [[ -z $end_date ]]; then
        printf '%-12s %s\n' "UNREADABLE" "$file"
        return
    fi

    end_epoch=$(date -u -d "$end_date" +%s 2>/dev/null) || end_epoch=0
    days=$(( (end_epoch - now_epoch) / 86400 ))

    local status
    if (( days < 0 ));   then status="EXPIRED"; expired+=("$(printf '%4dd  %s  %s' "$days" "$end_date" "$file")")
    elif (( days < 30 )); then status="WARN"
    else                       status="OK"
    fi

    printf '%-8s %4dd  %s  %s  [%s]\n' "$status" "$days" "$end_date" "$file" "$subject"
}

shopt -s nullglob globstar nocaseglob

for dir in "${dirs[@]}"; do
    [[ -d $dir ]] || { echo "skip: $dir is not a directory" >&2; continue; }

    while IFS= read -r -d '' f; do
        case ${f,,} in
            *.pem|*.crt|*.cer)        print_cert "$f" pem ;;
            *.der)                    print_cert "$f" der ;;
            *.p12|*.pfx)              print_cert "$f" p12 ;;
        esac
    done < <(find "$dir" -type f \( \
        -iname '*.pem' -o -iname '*.crt' -o -iname '*.cer' \
        -o -iname '*.der' -o -iname '*.p12' -o -iname '*.pfx' \) -print0)
done

echo
echo "=== Expired certificates (${#expired[@]}) ==="
if (( ${#expired[@]} == 0 )); then
    echo "(none)"
else
    printf '%s\n' "${expired[@]}"
fi
