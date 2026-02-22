#!/bin/sh
set -e
systemctl daemon-reload
systemctl enable mediadownloader
systemctl start mediadownloader
