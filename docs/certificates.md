# Creating a Local Root CA, Intermediate CA, and Server Certificates (OpenSSL)

This document describes how to create a complete local Public Key Infrastructure (PKI) using **OpenSSL on Linux/macOS (or WSL)**.
The example domains used are `books.example.org` and `api.example.org`.

The process consists of:

1. Creating a **Root CA** (self-signed).
2. Creating an **Intermediate CA** signed by the Root CA.
3. Generating **server certificates** signed by the Intermediate CA.
4. Configuring servers to provide the **server certificate + full chain**.

> ⚠️ All Root CA material (especially the private key) should be stored securely, preferably offline.
> For development purposes, keeping everything in one location is acceptable, but the logical separation should be understood.

---

## 0. Directory Structure Preparation

```bash
mkdir -p ~/ca/{root,intermediate}
cd ~/ca

# Root CA structure
mkdir -p root/{certs,crl,newcerts,private}
chmod 700 root/private
touch root/index.txt
echo 1000 > root/serial

# Intermediate CA structure
mkdir -p intermediate/{certs,crl,csr,newcerts,private}
chmod 700 intermediate/private
touch intermediate/index.txt
echo 1000 > intermediate/serial
```

---

## 1. Root CA Configuration

Create `root/openssl.cnf`:

```ini
[ ca ]
default_ca = CA_root

[ CA_root ]
dir               = ./root
certificate       = $dir/certs/ca.cert.pem
database          = $dir/index.txt
new_certs_dir     = $dir/newcerts
private_key       = $dir/private/ca.key.pem
serial            = $dir/serial
default_md        = sha256
policy            = policy_strict
default_days      = 3650
email_in_dn       = no
name_opt          = ca_default
cert_opt          = ca_default
copy_extensions   = copy
unique_subject    = no

[ policy_strict ]
countryName             = optional
stateOrProvinceName     = optional
organizationName        = optional
organizationalUnitName  = optional
commonName              = supplied

[ req ]
default_bits       = 4096
distinguished_name = req_distinguished_name
string_mask        = utf8only
default_md         = sha256
x509_extensions    = v3_ca

[ req_distinguished_name ]
commonName = Common Name

[ v3_ca ]
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid:always,issuer
basicConstraints = critical, CA:true
keyUsage = critical, keyCertSign, cRLSign
```

---

## 2. Creating the Root CA (Self-Signed)

```bash
cd ~/ca

# Root CA private key
openssl genrsa -out root/private/ca.key.pem 4096
chmod 600 root/private/ca.key.pem

# Self-signed Root CA certificate (10 years)
openssl req -config root/openssl.cnf \
  -key root/private/ca.key.pem \
  -new -x509 -days 3650 -sha256 \
  -extensions v3_ca \
  -out root/certs/ca.cert.pem \
  -subj "/CN=Bibliotheca Local Root CA"
```

---

## 3. Intermediate CA Configuration

Create `intermediate/openssl.cnf`:

```ini
[ ca ]
default_ca = CA_intermediate

[ CA_intermediate ]
dir               = ./intermediate
certificate       = $dir/certs/intermediate.cert.pem
database          = $dir/index.txt
new_certs_dir     = $dir/newcerts
private_key       = $dir/private/intermediate.key.pem
serial            = $dir/serial
default_md        = sha256
policy            = policy_loose
default_days      = 1825
email_in_dn       = no
name_opt          = ca_default
cert_opt          = ca_default
copy_extensions   = copy
unique_subject    = no

[ policy_loose ]
countryName             = optional
stateOrProvinceName     = optional
organizationName        = optional
organizationalUnitName  = optional
commonName              = supplied

[ req ]
default_bits       = 4096
distinguished_name = req_distinguished_name
string_mask        = utf8only
default_md         = sha256
x509_extensions    = v3_intermediate_ca

[ req_distinguished_name ]
commonName = Common Name

[ v3_intermediate_ca ]
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid:always,issuer
basicConstraints = critical, CA:true, pathlen:0
keyUsage = critical, keyCertSign, cRLSign

[ v3_server ]
basicConstraints = CA:false
nsCertType = server
nsComment = "Bibliotheca Local Server Cert"
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
```

---

## 4. Intermediate CA Key and CSR

```bash
openssl genrsa -out intermediate/private/intermediate.key.pem 4096
chmod 600 intermediate/private/intermediate.key.pem

openssl req -config intermediate/openssl.cnf \
  -new -sha256 \
  -key intermediate/private/intermediate.key.pem \
  -out intermediate/csr/intermediate.csr.pem \
  -subj "/CN=Bibliotheca Intermediate CA"
```

---

## 5. Signing the Intermediate CA

```bash
openssl ca -config root/openssl.cnf \
  -extensions v3_ca \
  -days 3650 -notext -md sha256 \
  -in intermediate/csr/intermediate.csr.pem \
  -out intermediate/certs/intermediate.cert.pem

chmod 644 intermediate/certs/intermediate.cert.pem
```

Create the full CA chain:

```bash
cat intermediate/certs/intermediate.cert.pem root/certs/ca.cert.pem \
  > intermediate/certs/ca-chain.cert.pem
```

---

## 6. Generating Server Certificates

### 6.1 `books.example.org`

Private key:

```bash
openssl genrsa -out intermediate/private/books.example.org.key.pem 2048
chmod 600 intermediate/private/books.example.org.key.pem
```

CSR:

```bash
openssl req -new -sha256 \
  -key intermediate/private/books.example.org.key.pem \
  -out intermediate/csr/books.example.org.csr.pem \
  -subj "/CN=books.example.org"
```

SAN extension file: `intermediate/books.example.org.ext`

```ini
[ v3_server ]
basicConstraints = CA:false
subjectKeyIdentifier=hash
authorityKeyIdentifier=keyid,issuer
keyUsage = critical, digitalSignature, keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = @alt_names

[ alt_names ]
DNS.1 = books.example.org
```

Sign:

```bash
openssl ca -config intermediate/openssl.cnf \
  -extensions v3_server \
  -extfile intermediate/books.example.org.ext \
  -days 825 -notext -md sha256 \
  -in intermediate/csr/books.example.org.csr.pem \
  -out intermediate/certs/books.example.org.cert.pem

chmod 644 intermediate/certs/books.example.org.cert.pem
```

### 6.2 `api.example.org`

Repeat the same process with the corresponding filenames and CN.

---

## 7. Server Configuration Requirements

For each domain:

| Purpose                                 | File                                    |
| --------------------------------------- | --------------------------------------- |
| Server certificate                      | `intermediate/certs/<domain>.cert.pem`  |
| Private key                             | `intermediate/private/<domain>.key.pem` |
| Certificate chain (Intermediate + Root) | `intermediate/certs/ca-chain.cert.pem`  |

Example (Nginx):

```nginx
server {
    listen 443 ssl;
    server_name books.example.org;

    ssl_certificate     /etc/ssl/books.example.org.cert.pem;
    ssl_certificate_key /etc/ssl/books.example.org.key.pem;

    # Full certificate chain
    ssl_trusted_certificate /etc/ssl/ca-chain.cert.pem;

    ...
}
```

Some environments allow concatenating the server certificate and intermediate certificate into a single file; the critical requirement is that the **full chain** is delivered to the client.

## OIDC certificates

The production API uses two separate certificates to sign and encrypt OIDC
tokens. These certificates are independent of the TLS certificates above and
are required when a reverse proxy terminates TLS as well.

Copy `create-oidc-certificates.sh` from the repository root to the deployment
directory on the Raspberry Pi and run it there with OpenSSL installed:

```sh
cd /opt/bibliotheca
sh ./create-oidc-certificates.sh
```

The script prompts for an export password for each new certificate and preserves
existing files. It creates two self-signed PKCS#12 files with private keys. With
the Compose mount `./cert:/app/cert/`, set the following in the API's mounted
`appsettings.json` under `Authentication`:

| Setting | Value |
| --- | --- |
| `SigningCertificate:Path` | `/app/cert/oidc-signing.pfx` |
| `EncryptionCertificate:Path` | `/app/cert/oidc-encryption.pfx` |
| `DataProtectionKeyPath` | `/app/data/keys` |

Store the export passwords as `OIDC_SIGNING_PASSWORD` and
`OIDC_ENCRYPTION_PASSWORD` in the deployment's protected `.env` file. Add these
entries to the `api` service's existing `environment` block:

```yaml
Authentication__SigningCertificate__Password: "${OIDC_SIGNING_PASSWORD:?Set OIDC_SIGNING_PASSWORD}"
Authentication__EncryptionCertificate__Password: "${OIDC_ENCRYPTION_PASSWORD:?Set OIDC_ENCRYPTION_PASSWORD}"
```

Ensure the API container can read the generated files and write to the persistent
key directory. See [Running](../README.md#running) for the remaining production
authentication settings and certificate replacement guidance.
