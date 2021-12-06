import React from 'react';
import { Container, Navbar, Nav, Form, FormControl, Button } from 'react-bootstrap';
import { useAppStateStore } from "../provider/AppStateStore";

function GlobalNav() {

    const { isAuthenticated, authenticate, logout } = useAppStateStore();

    const handleLogin = () => {
        authenticate("a");
    }

    const handleLogout = () => {
        logout();
    }

    return (
        <Navbar collapseOnSelect expand="lg" bg="dark" variant="dark">
            <Container>
                <Navbar.Brand href="#home">Youverse</Navbar.Brand>
                <Navbar.Toggle aria-controls="responsive-navbar-nav" />
                <Navbar.Collapse id="responsive-navbar-nav">
                    <Nav className="me-auto">
                        {isAuthenticated && <Nav.Link href="blog">Blog</Nav.Link>}
                    </Nav>
                    <Nav>
                        <Form className="d-flex">
                            {!isAuthenticated && <>

                                <FormControl
                                    type="text"
                                    placeholder="youverse id"
                                    className="me-2"
                                    size="sm"
                                    aria-label="youverse id"
                                />
                                <Button size="sm" className="me-1" variant="outline-success" onClick={handleLogin}>Login</Button>
                                <Button size="sm" variant="outline-info">Signup</Button>
                            </>}

                            {isAuthenticated && <>

                                <Button size="sm" className="me-1" variant="outline-success" onClick={handleLogout}>Logout</Button>
                            </>}
                        </Form>
                    </Nav>
                </Navbar.Collapse>
            </Container>
        </Navbar>
    );
}

export default GlobalNav;
