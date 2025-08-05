#!/bin/bash
set -e
DIR=$(dirname "$0")/certs
QM=QM1
PASSWORD=passw0rd
rm -rf "$DIR"
mkdir -p "$DIR/keys/$QM" "$DIR/client"
# Create queue manager key repository and certificate
docker run --rm -v "$DIR/keys/$QM":/certs ibmcom/mq:latest \
  bash -c "runmqakm -keydb -create -db /certs/key.kdb -pw $PASSWORD -type kdb -stash && \
           runmqakm -cert -create -db /certs/key.kdb -pw $PASSWORD -label ibmwebspheremqqm1 -dn 'CN=$QM' && \
           runmqakm -cert -extract -db /certs/key.kdb -pw $PASSWORD -label ibmwebspheremqqm1 -target /certs/qmgr.crt -format ascii"
# Create client trust store and import queue manager certificate
docker run --rm -v "$DIR":/certs ibmcom/mq:latest \
  bash -c "runmqakm -keydb -create -db /certs/client/client.kdb -pw $PASSWORD -type kdb -stash && \
           runmqakm -cert -add -db /certs/client/client.kdb -pw $PASSWORD -label qmgrcert -file /certs/keys/$QM/qmgr.crt -format ascii"
