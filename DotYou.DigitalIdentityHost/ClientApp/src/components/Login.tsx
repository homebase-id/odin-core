import React, {useState} from 'react';
import {Button, Card, FormControl, Image} from "react-bootstrap";
import {createAuthenticationProvider} from "../provider/AuthenticationProvider";
import {useAppStateStore} from "../provider/AppStateStore";

function Login(props: any) {

    const [passwordText, setPasswordText] = useState<string>("");
    const [errorText, setErrorText] = useState<string>("");
    const {authenticate} = useAppStateStore();

    async function handleLoginClick() {
        if (!passwordText) {
            setErrorText("Please enter your password");
            return;
        }

        authenticate(passwordText).then(success => {
            if (!success) {
                setErrorText("Invalid password");
            }
        });
    }

    async function handleForcePassword() {
        let auth = createAuthenticationProvider();
        let success = await auth.forceSetPassword_temp("a");
        if (success) {
            alert("Password was successfully set to a\n\nPress Sign in now");
            setPasswordText("a")
        }
    }

    return <Card className="align-content-center text-center align-middle">
        <Card.Body>
            <form className="form-signin">
                {/*<Image className="mb-4" src="" alt="" width="72" height="72"/>*/}
                <h1 className="h3 mb-3 font-weight-normal">Please sign in</h1>

                {errorText && (<div className="alert alert-primary alert-dismissible fade show">
                    {errorText}
                    <button type="button" className="close" data-dismiss="alert">&times;</button>
                </div>)}

                <FormControl placeholder="Enter Password" aria-label="Password" onChange={e => setPasswordText(e.target.value)}/>
                <Button className="btn btn-lg btn-primary btn-block mt-3" onClick={handleLoginClick}>Sign in</Button>
                <br/>
                <br/>
                <Button className="btn mt-10" onClick={handleForcePassword}>Force password to "a"</Button>
                {/*<p className="mt-5 mb-3 text-muted">&copy; 2017-2018</p>*/}
            </form>
        </Card.Body>
    </Card>

}

export default Login;