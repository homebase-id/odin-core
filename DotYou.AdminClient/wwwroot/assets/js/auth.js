/**
 * @param {string} strPassword The clear text password
 * @param {Uint8Array} salt    The salt
 * @param {string} hash        The Hash model, e.g. ["SHA-256" | "SHA-512"]
 * @param {int} iterations     Number of iterations
 * @param {int} len            The output length in bytes, e.g. 16
 */
async function pbkdf2(strPassword, salt, hash, iterations, len) {
    var password = new TextEncoder().encode(strPassword);

    var ik = await window.crypto.subtle.importKey("raw", password, {name: "PBKDF2"}, false, ["deriveBits"]);
    var dk = await window.crypto.subtle.deriveBits(
        {
            name: "PBKDF2",
            hash: hash,
            salt: salt,
            iterations: iterations
        },
        ik,
        len * 8);  // Bytes to bits

    return new Uint8Array(dk);
}

function wrapPbkdf2HmacSha256(password, saltArray64, iterations, len) {
    var u8salt = Uint8Array.from(atob(saltArray64), c => c.charCodeAt(0));

    return pbkdf2(password, u8salt, "SHA-256", iterations, len).then(hashed => {
            var base64 = btoa(String.fromCharCode.apply(null, hashed));
            return base64;
        }
    );
}


// Validate that the implementation returns the same as in C# / .net core
function test_pbkdf2() {
    const areEqual = (first, second) =>
        first.length === second.length && first.every((value, index) => value === second[index]);

    var salt = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    var expected = new Uint8Array([162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204]);
    const iterations = 100000;

    return wrapPbkdf2HmacSha256("EnSøienØ", btoa(String.fromCharCode.apply(null, salt)), iterations, 16).then(hashed64 => {

        var baHash = Uint8Array.from(atob(hashed64), c => c.charCodeAt(0));

        console.log("Hashed64 " + baHash + " ?= " + expected);

        return areEqual(expected, baHash);
    }
    );
}


// ==========================================
// AES-CBC
// ==========================================

// DONT USE THIS FUNCTION, ONLY FOR TESTING
async function AesCbc_EncryptIv(u8aData, u8aKey, u8aIv) {
    let key = await crypto.subtle.importKey(
        "raw",
        u8aKey,
        {   //this is the algorithm options
            name: "AES-CBC",
        },
        false, //whether the key is extractable (i.e. can be used in exportKey)
        ["encrypt", "decrypt"] //can be "encrypt", "decrypt", "wrapKey", or "unwrapKey"
    );

    let cipher = await crypto.subtle.encrypt(
        {
            name: "AES-CBC",
            iv: u8aIv,
        },
        key, //from generateKey or importKey above
        u8aData //ArrayBuffer of data you want to encrypt
    );

    return { cipher: (new Uint8Array(cipher)), iv: u8aIv };
}

async function AesCbc_Encrypt(u8aData, u8aKey)
{
    let key = await crypto.subtle.importKey(
        "raw",
        u8aKey,
        {   //this is the algorithm options
            name: "AES-CBC",
        },
        false, //whether the key is extractable (i.e. can be used in exportKey)
        ["encrypt", "decrypt"] //can be "encrypt", "decrypt", "wrapKey", or "unwrapKey"
    );

    let iv = window.crypto.getRandomValues(new Uint8Array(16));
    // console.log("IV = " + iv);

    let cipher = await crypto.subtle.encrypt(
        {
            name: "AES-CBC",
            iv: iv,
        },
        key, //from generateKey or importKey above
        u8aData //ArrayBuffer of data you want to encrypt
    );

    return { cipher: (new Uint8Array(cipher)), iv: iv };
}

async function AesCbc_Decrypt(u8aCipher, u8aKey, iv) {
    // console.log("Decrypt IV = " + iv);
    let key = await crypto.subtle.importKey(
        "raw",
        u8aKey,
        {   //this is the algorithm options
            name: "AES-CBC",
        },
        false, //whether the key is extractable (i.e. can be used in exportKey)
        ["encrypt", "decrypt"] //can be "encrypt", "decrypt", "wrapKey", or "unwrapKey"
    );

    let decrypted = await crypto.subtle.decrypt(
        {
            name: "AES-CBC",
            iv: iv, //The initialization vector you used to encrypt
        },
        key, //from generateKey or importKey above
        u8aCipher //ArrayBuffer of the data
    );

    return new Uint8Array(decrypted);
}

// Validate that the implementation returns the same as in C# / .net core
function test_AesCbc() {
    const areEqual = (first, second) =>
        first.length === second.length && first.every((value, index) => value === second[index]);


    // First do a round-trip encrypt / decrypt
    //
    //
    var key = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    var iv = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    var testData = new Uint8Array([162, 146, 244, 255, 127, 128, 0, 42, 7, 0]);
    var expectedCipher = new Uint8Array([88, 229, 118, 198, 59, 215, 71, 157, 114, 102, 238, 38, 203, 251, 48, 157]);

    AesCbc_EncryptIv(testData, key, iv).then(myobj =>
    {
        console.log("Cipher " + myobj.cipher);
        console.log("Expected " + expectedCipher);
        console.log("Cipher and .NET AreEqual? " + areEqual(expectedCipher, myobj.cipher));

        AesCbc_Decrypt(myobj.cipher, key, myobj.iv).then(u8aDecrypted => {
            console.log("Original " + testData + " ?= " + u8aDecrypted);

            return areEqual(testData, u8aDecrypted);
        });
    });
}

// ===============================================
// RSA OAEP
// ===============================================
async function test_RsaGenerateKey(bits)
{
    var key = await window.crypto.subtle.generateKey(
        {
            name: "RSA-OAEP",
            modulusLength: bits, //can be 1024, 2048, or 4096
            publicExponent: new Uint8Array([0x01, 0x00, 0x01]),
            hash: { name: "SHA-256" }, //can be "SHA-1", "SHA-256", "SHA-384", or "SHA-512"
        },
        false, //whether the key is extractable (i.e. can be used in exportKey)
        ["encrypt", "decrypt"] //must be ["encrypt", "decrypt"] or ["wrapKey", "unwrapKey"]
    );

    console.log(key);
    console.log(key.publicKey);
    console.log(key.privateKey);

    return key;
}


async function RsaOaepEncrypt(publicKey, data) {
    var encrypted = await window.crypto.subtle.encrypt(
        {
            name: "RSA-OAEP",
            //label: Uint8Array([...]) //optional
        },
        publicKey, //from generateKey or importKey above
        data //ArrayBuffer of data you want to encrypt
    );

    var u8a = new Uint8Array(encrypted);
    console.log(u8a);

    return u8a;
}



async function RsaOaepDecrypt(privateKey, data) {
    var decrypted = await window.crypto.subtle.encrypt(
        {
            name: "RSA-OAEP",
            //label: Uint8Array([...]) //optional
        },
        privateKey, //from generateKey or importKey above
        data //ArrayBuffer of the data
    );

    //returns an ArrayBuffer containing the decrypted data
    console.log(new Uint8Array(decrypted));

    var u8a = new Uint8Array(decrypted);

    return u8a;
}
