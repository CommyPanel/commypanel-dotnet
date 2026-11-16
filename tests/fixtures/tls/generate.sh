#!/usr/bin/env bash
# Regenerates the TEST-ONLY certificates used by the TLS tests. Nothing here protects anything: the private keys are
# committed on purpose, the certificates are for localhost only, and the authorities' own keys are thrown away.
# Needs openssl. Valid for 20 years: run this again if the tests ever complain about expiry.
set -euo pipefail
cd "$(dirname "$0")"
days=7300

make_ca() {  # name
    openssl genrsa -out "$1.key" 2048 2>/dev/null
    cat > "$1.cnf" <<CNF
[req]
distinguished_name = dn
x509_extensions = ext
prompt = no
[dn]
CN = CommyPanel test CA ($1, do not trust)
[ext]
basicConstraints = critical,CA:TRUE
keyUsage = critical,keyCertSign,cRLSign
subjectKeyIdentifier = hash
CNF
    openssl req -x509 -new -key "$1.key" -sha256 -days "$days" -config "$1.cnf" -out "$1.pem"
    rm "$1.cnf"
}

make_leaf() {  # file-name  ca-name  common-name  subjectAltName
    openssl genrsa 2048 2>/dev/null | openssl pkcs8 -topk8 -nocrypt -out "$1.key"
    openssl req -new -key "$1.key" -subj "/CN=$3" -out "$1.csr"
    cat > "$1.ext" <<CNF
basicConstraints = CA:FALSE
keyUsage = digitalSignature,keyEncipherment
extendedKeyUsage = serverAuth
subjectAltName = $4
CNF
    openssl x509 -req -in "$1.csr" -CA "$2.pem" -CAkey "$2.key" -CAcreateserial -days "$days" -sha256 -extfile "$1.ext" -out "$1.pem" 2>/dev/null
    rm "$1.csr" "$1.ext"
}

make_ca ca
make_ca other-ca
make_leaf server ca localhost "DNS:localhost,IP:127.0.0.1,IP:::1"
make_leaf wrong-host ca wrong.example "DNS:wrong.example"
make_leaf other-server other-ca localhost "DNS:localhost,IP:127.0.0.1,IP:::1"

rm -f ca.key other-ca.key ca.srl other-ca.srl
ls -1 *.pem *.key
