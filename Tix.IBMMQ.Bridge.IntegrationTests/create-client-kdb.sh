#!/bin/bash

# This script creates a client key database (.kdb) and imports the
# queue manager's public certificate into it. The client application
# will use this KDB to establish a TLS connection to the queue manager.

set -e

# The directory of this script
DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" &> /dev/null && pwd )"

# The output directory for the generated files
# Defaults to the script's directory
OUT_DIR=${1:-$DIR}

# The path to the queue manager's public certificate
# This is the certificate that the client needs to trust.
QMGR_CERT="$DIR/../../../../certs/keys/QM1/qmgr.crt"

# The name of the client key database (without extension)
KDB_STEM="$OUT_DIR/client"

# The password for the key database
# This can be any string. The password will be stashed in a .sth file.
KDB_PW="password"

# The Docker image to use for running runmqckm
IMAGE="ibmcom/mq:latest"

# Check if the queue manager certificate exists
if [ ! -f "$QMGR_CERT" ]; then
    echo "Queue manager certificate not found at $QMGR_CERT"
    exit 1
fi

# Create the output directory if it doesn't exist
mkdir -p "$OUT_DIR"

echo "Creating client key database at ${KDB_STEM}.kdb"

# The docker command will mount the output directory and the certs directory
# and run the runmqckm commands inside the container.
docker run --rm \
    -v "$OUT_DIR:/kdb" \
    -v "$(dirname "$QMGR_CERT"):/certs" \
    "$IMAGE" \
    bash -c "
        # Create a new key database
        runmqckm -keydb -create -db /kdb/client.kdb -pw $KDB_PW -type kdb -stash

        # Import the queue manager's certificate
        runmqckm -cert -add -db /kdb/client.kdb -stashed -label qmgr -file /certs/qmgr.crt
    "

echo "Client key database created successfully."
