import React, {useEffect, useState} from 'react';
import {Button, Col, Form, FormControl, Row} from "react-bootstrap";
import axios from "axios";
import {createTransitProvider, TransitProvider} from "../provider/TransitProvider";

type Props = {};

function Transit(props: Props) {
    const [recipient, setRecipient] = useState<string>("samwisegamgee.me");
    const [message, setMessage] = useState<string>("get to the choppah");
    
    useEffect(() => {
        loadOutbox();
    }, []);

    function loadOutbox() {
        //axios.get()
    }

    const handleSendMessage = () => {
        let tp = createTransitProvider();
        return tp.sendPayload([recipient], message, null).then(result => {
            console.log(result);
        });
    }

    return <div>

        <h3>Transit</h3>
        <Form>

            <Row className="g-2 p-2">
                <Col md>
                    <Form.Control type="text" value={recipient} placeholder="Recipient" onChange={e => setRecipient(e.target.value)}/>
                </Col>
            </Row>
            <Row className="g-2 p-2">
                <Col md>
                    <Form.Control type="text" value={message} placeholder="Message" onChange={e => setMessage(e.target.value)}/>
                </Col>
            </Row>

            <Form.Group controlId="formFile" className="mb-3 p-2">
                <Form.Label>A file: </Form.Label>
                <Form.Control type="file"/>
            </Form.Group>

            <Button className="btn btn-lg btn-primary btn-block mt-3" onClick={handleSendMessage}>Send</Button>
        </Form>

    </div>
}

export default Transit;