import React from 'react';
import {Container, Navbar, Nav, Form, FormControl, Button} from 'react-bootstrap';

function GlobalNav() {
    return (
        <Navbar collapseOnSelect expand="lg" bg="dark" variant="dark">
            <Container>
                <Navbar.Brand href="#home">Youverse</Navbar.Brand>
                <Navbar.Toggle aria-controls="responsive-navbar-nav"/>
                <Navbar.Collapse id="responsive-navbar-nav">
                    <Nav className="me-auto">
                    </Nav>
                    <Nav>
                        <Form className="d-flex">
                            <FormControl
                                type="text"
                                placeholder="youverse id"
                                className="me-2"
                                size="sm"
                                aria-label="youverse id"
                            />
                            <Button size="sm" className="me-1" variant="outline-success" >Login</Button>
                            <Button size="sm" variant="outline-info">Signup</Button>
                        </Form>
                    </Nav>
                </Navbar.Collapse>
            </Container>
        </Navbar>
    );
}

export default GlobalNav;
