import React, {useEffect, useState} from 'react';
import {Button, Col, Form, FormControl, Row} from "react-bootstrap";

type Props = {};

function Profile(props: Props) {
    const [data, setData] = useState("some data");
    const [firstName, setFirstName] = useState<string>("");
    const [surname, setSurname] = useState<string>("");
    function handleSaveProfile() {
        
    }

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
                    <Form.Control type="text" placeholder="First name" onChange={e => setFirstName(e.target.value)}/>
                </Col>
                <Col md>
                    <Form.Control type="text" placeholder="Surname" onChange={e => setSurname(e.target.value)}/>
                </Col>
            </Row>

            {/*<Form.Group controlId="formFile" className="mb-3">*/}
            {/*    <Form.Label>Profile Picture</Form.Label>*/}
            {/*    <Form.Control type="file"/>*/}
            {/*</Form.Group>*/}

            <Button className="btn btn-lg btn-primary btn-block mt-3" onClick={handleSaveProfile}>Save</Button>
        </Form>
    </div>
}

export default Profile;