#!/usr/bin/env python3
"""Try to open an mRemoteNG connection file with the published default key.

Task 8.4 asks for an *independent* check that a migrated file is no longer readable
under `mR3m`. Asking mRemoteNG whether mRemoteNG still uses mR3m proves very little,
so this reimplements the classic format from scratch and depends on nothing in the
repository:

    key        = PBKDF2(password, salt, iterations, prf) -> 32 bytes
    ciphertext = base64( salt[16] || nonce[16] || aes_256_gcm(plaintext) || tag[16] )
    aad        = salt

The salt is authenticated as associated data, and the nonce is 16 bytes rather than
the usual 12 — which is why this uses `cryptography`'s low-level Cipher API. .NET's
own AesGcm refuses any nonce but 12 bytes, so a script written against it would fail
on every file and prove nothing.

Requires: pip install cryptography

    python check-legacy-key.py <confCons.xml>

Exit code 0 means the published key did NOT open the file, which is the result task
8.4 wants from a migrated file. Exit code 1 means it did.

Run it against a known classic file FIRST (see --self-test). A script that fails to
decrypt everything, including files that really are under mR3m, would report exactly
the same success on a migrated file and mean nothing at all.
"""

import base64
import sys
import xml.etree.ElementTree as ET

try:
    from cryptography.hazmat.primitives import hashes
    from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    from cryptography.hazmat.primitives.kdf.pbkdf2 import PBKDF2HMAC
except ImportError:
    sys.exit("needs the 'cryptography' package: pip install cryptography")

PUBLISHED_KEY = b"mR3m"  # ConnectionFileDefaults.LegacyEncryptionKey, CVE-2023-30367

SALT_LEN = 16
NONCE_LEN = 16
TAG_LEN = 16

PRFS = {"SHA1": hashes.SHA1, "SHA256": hashes.SHA256, "SHA384": hashes.SHA384, "SHA512": hashes.SHA512}


def try_decrypt(b64, password, iterations, prf_name):
    """Return the plaintext, or None if this key does not open it."""
    try:
        blob = base64.b64decode(b64)
    except Exception:
        return None
    if len(blob) < SALT_LEN + NONCE_LEN + TAG_LEN:
        return None

    salt = blob[:SALT_LEN]
    nonce = blob[SALT_LEN:SALT_LEN + NONCE_LEN]
    body = blob[SALT_LEN + NONCE_LEN:]
    ct, tag = body[:-TAG_LEN], body[-TAG_LEN:]

    prf = PRFS.get(prf_name.upper().replace("-", ""), hashes.SHA1)
    key = PBKDF2HMAC(algorithm=prf(), length=32, salt=salt, iterations=iterations).derive(password)

    decryptor = Cipher(algorithms.AES(key), modes.GCM(nonce, tag)).decryptor()
    decryptor.authenticate_additional_data(salt)
    try:
        return (decryptor.update(ct) + decryptor.finalize()).decode("utf-8", "replace")
    except Exception:
        return None  # tag mismatch: this key did not encrypt this


def parameters(root):
    """The KDF parameters the file records, with the historical defaults for absence."""
    prf = root.get("KdfPrf") or "SHA1"
    recorded = root.get("KdfIterations")
    # A file that records nothing was written before the format carried it. 1000 is the
    # historical default; 600000 is this fork's. Trying both keeps a negative result from
    # being an artefact of guessing wrong.
    iterations = [int(recorded)] if recorded else [1000, 600000]
    return iterations, prf


def inspect(path):
    root = ET.parse(path).getroot()
    iteration_list, prf = parameters(root)

    print(f"file        : {path}")
    print(f"KdfPrf      : {prf}{'' if root.get('KdfPrf') else '  (absent, assumed)'}")
    print(f"KdfIterations: {root.get('KdfIterations') or f'absent, trying {iteration_list}'}")
    print(f"StorageFormat: {root.get('StorageFormat') or 'absent (classic)'}")
    print(f"protectors  : machine={'yes' if root.get('KeyProtectorMachine') else 'no'}, "
          f"recovery={'yes' if root.get('KeyProtectorRecovery') else 'no'}")
    print()

    targets = [("root sentinel (Protected)", root.get("Protected"))]
    for node in root.iter():
        pw = node.get("Password")
        if pw:
            targets.append((f"password of \"{node.get('Name')}\"", pw))
    targets = targets[:6]

    opened = []
    for label, value in targets:
        if not value:
            print(f"  [skip] {label}: empty")
            continue
        for iterations in iteration_list:
            plaintext = try_decrypt(value, PUBLISHED_KEY, iterations, prf)
            if plaintext is not None:
                print(f"  [OPEN] {label}: {plaintext!r}   (at {iterations} iterations)")
                opened.append(label)
                break
        else:
            print(f"  [shut] {label}: the published key does not open this")

    print()
    if opened:
        print("RESULT: the published key mR3m OPENS this file. Anyone holding a copy can read it.")
        return 1

    print("RESULT: the published key mR3m does not open this file.")
    return 0


def self_test():
    """Prove the script can succeed before its failure is taken as evidence.

    Uses a file in the repository written by real mRemoteNG under the published key.
    If this does not report OPEN, the script is broken and every other result it gives
    is worthless.
    """
    import os
    here = os.path.dirname(os.path.abspath(__file__))
    sample = os.path.join(here, "..", "..", "..", "..",
                          "mRemoteNGTests", "Resources", "confCons_v2_6.xml")
    sample = os.path.normpath(sample)
    if not os.path.exists(sample):
        sys.exit(f"self-test file not found: {sample}\nRun the script from a repository checkout.")

    print("=== SELF-TEST: a file that really is under the published key ===\n")
    if inspect(sample) != 1:
        print("\nSELF-TEST FAILED: the script cannot open a file it should. Do not trust its results.")
        return 1
    print("\nSELF-TEST PASSED: the script opens a file under the published key, so a 'shut'")
    print("result on another file means that file is genuinely not under it.")
    return 0


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    if sys.argv[1] == "--self-test":
        sys.exit(self_test())
    sys.exit(inspect(sys.argv[1]))
