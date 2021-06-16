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

            //console.log(hashed);

            var base64 = btoa(String.fromCharCode.apply(null, hashed));

            //console.log(base64);
            return base64;
        }
    );
}


// Validate that the implementation returns the same as in C# / .net core
async function test_pbkdf2() {
    const areEqual = (first, second) =>
        first.length === second.length && first.every((value, index) => value === second[index]);

    var salt = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16]);
    var expected = new Uint8Array([162, 146, 244, 243, 106, 138, 115, 194, 11, 233, 94, 27, 79, 215, 36, 204]);
    const iterations = 100000;

    var qq = await pbkdf2("EnSøienØ", salt, "SHA-256", iterations, 16);
    console.log("Result:   " + qq);
    console.log("Expected: " + expected);
    console.log("Pass test = " + areEqual(qq, expected));
    return qq;
}


// OBSOLETE
function OBSOLETEwrapPbkdf2HmacSha256(password, saltArray64, iterations, len) {

    //var u8pwd = Uint8Array.from(atob(password), c => c.charCodeAt(0));
    var u8salt = Uint8Array.from(atob(saltArray64), c => c.charCodeAt(0));
    var passwordArray = new TextEncoder().encode(password);

    var hashed = asmCrypto.Pbkdf2HmacSha256(passwordArray, u8salt, iterations, len);

    var base64 = btoa(String.fromCharCode.apply(null, hashed));

    //console.log(hashed);
    //console.log(base64);

    return base64;
}

