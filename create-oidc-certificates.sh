#!/bin/sh
set -eu

# Run from the deployment directory, or pass the certificate directory.
cert_dir=${1:-cert}
umask 077
mkdir -p -- "$cert_dir"
work_dir=$(mktemp -d "$cert_dir/.oidc-XXXXXX")
trap 'rm -f -- "$work_dir/key.pem" "$work_dir/cert.pem" "$work_dir/cert.pfx"; rmdir -- "$work_dir"' EXIT
trap 'exit 1' HUP INT TERM

for purpose in signing encryption; do
    output="$cert_dir/oidc-$purpose.pfx"
    if [ -e "$output" ] || [ -L "$output" ]; then
        printf 'Already exists: %s\n' "$output"
        continue
    fi

    case "$purpose" in
        signing) key_usage=digitalSignature ;;
        encryption) key_usage=keyEncipherment ;;
    esac

    openssl req -x509 -newkey rsa:3072 -nodes -sha256 -days 730 \
        -subj "/CN=Bibliotheca OIDC $purpose" \
        -addext 'basicConstraints=critical,CA:FALSE' \
        -addext "keyUsage=critical,$key_usage" \
        -keyout "$work_dir/key.pem" -out "$work_dir/cert.pem"

    printf '\nChoose the export password for %s\n' "$output"
    openssl pkcs12 -export -inkey "$work_dir/key.pem" \
        -in "$work_dir/cert.pem" -out "$work_dir/cert.pfx"
    ln -T -- "$work_dir/cert.pfx" "$output"
    rm -- "$work_dir/cert.pfx"
    printf 'Created: %s\n' "$output"
done
