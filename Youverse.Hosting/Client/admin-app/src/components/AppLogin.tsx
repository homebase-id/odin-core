import React, {Component, useState} from 'react';
import {Button, Card, Col, Container, FormControl, Row} from "react-bootstrap";
import {useAppStateStore} from "../provider/AppStateStore";


function AppLogin(props: any) {

    const [passwordText, setPasswordText] = useState<string>("");
    const [errorText, setErrorText] = useState<string>("");
    const state = useAppStateStore();


    function handleLoginClick() {
        if (!passwordText) {
            setErrorText("Please enter your password");
            return;
        }

        state.authenticateDevice(passwordText).then(success => {
            if (success) {
                window.location.href = "dotyou://auth/" + state.deviceToken;
                return;
            }

            setErrorText("Invalid password");
        })
    }

    return (
        <Container className="h-100 align-content-center text-center">
            <Card className="align-content-center text-center align-middle">
                <Card.Body>
                    <form className="form-signin">
                        {/*<Image className="mb-4" src="" alt="" width="72" height="72"/>*/}
                        <h1 className="h3 mb-3 font-weight-normal">Please sign in</h1>

                        {errorText && (<div className="alert alert-primary alert-dismissible fade show">
                            {errorText}
                            <button type="button" className="close" data-dismiss="alert">&times;</button>
                        </div>)}

                        <FormControl type="password" placeholder="Enter Password" aria-label="Password" onChange={e => setPasswordText(e.target.value)}/>
                        <Button className="btn btn-lg btn-primary btn-block mt-3" onClick={handleLoginClick}>Sign in</Button>
                    </form>
                </Card.Body>
            </Card>
        </Container>
    );
}

export default AppLogin;