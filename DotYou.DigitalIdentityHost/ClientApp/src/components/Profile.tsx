import React, {useState} from 'react';
import {Col, Form, Row} from "react-bootstrap";

type Props = {};

function Profile(props: Props) {
    const [data, setData] = useState("some data");
    /*
    public picture
    public first and last name
    public tagline
    
    connected friends picture
    connected first and last name
    */
    return <div>

        <h3>Public Info</h3>
        <h5 className="text-muted">This will be seen by anyone on the internet</h5>
        <Form>

            <Row className="g-2">
                <Col md>
                    <Form.Control type="text" placeholder="First name"/>
                </Col>
                <Col md>
                    <Form.Control type="text" placeholder="Surname"/>
                </Col>
            </Row>

            <Form.Group controlId="formFile" className="mb-3">
                <Form.Label>Profile Picture</Form.Label>
                <Form.Control type="file"/>
            </Form.Group>

        </Form>
    </div>
}

export default Profile;