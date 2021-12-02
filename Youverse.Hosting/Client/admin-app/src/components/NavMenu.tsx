import React, {useState} from 'react';
import './NavMenu.css';
import {Container, Navbar, Nav, Modal, NavDropdown, DropdownButton, Offcanvas, Form, Button, FormControl, Row} from "react-bootstrap";
import {useAppStateStore} from "../provider/AppStateStore";

function NavMenu(props: any) {

    const [collapsed, setCollapsed] = useState<boolean>(false);
    const [showLogoutModal, setShowLogoutModal] = useState<boolean>(false);
    const {logout} = useAppStateStore();

    const toggleNavbar = () => {
        setCollapsed(!collapsed);
    }

    const handleLogout = () => {
        setShowLogoutModal(true);
        logout().then(success => {
            if (success) {
                window.location.href = "/"
            }

        });
    }
    return (
        <header>
            <Navbar bg="light" expand="lg" sticky="top">
                <Container>
                    <Navbar.Brand href="#">Youverse Admin</Navbar.Brand>
                    <Navbar.Toggle aria-controls="offcanvasNavbar"/>
                   
                    <Nav className="justify-content-end" activeKey="/home">
                        <Nav.Link href="/home">Active</Nav.Link>
                        <Nav.Link eventKey="link-1">Link</Nav.Link>
                        <Nav.Link eventKey="link-2">Link</Nav.Link>
                    </Nav>

                    <Navbar.Offcanvas
                        className="px-2 m-3"
                        id="offcanvasNavbar"
                        aria-labelledby="offcanvasNavbarLabel"
                        placement="start">
                        
                        <Offcanvas.Header closeButton>
                            <Offcanvas.Title id="offcanvasNavbarLabel">Youverse</Offcanvas.Title>
                        </Offcanvas.Header>
                        <Form className="d-flex">
                            <FormControl
                                type="search"
                                placeholder="Search"
                                className="me-2"
                                aria-label="Search"
                            />
                            <Button variant="outline-success">Search</Button>
                        </Form>
                        <Offcanvas.Body>
                            <Nav className="justify-content-end flex-grow-1 pe-3">
                                <Nav.Link href="/admin">Dashboard</Nav.Link>
                                <Nav.Link href="/mynetwork">My Network</Nav.Link>
                                <Nav.Link href="/profile">Profile</Nav.Link>
                                <Nav.Link href="/blog">Blog</Nav.Link>
                                <Nav.Link href="/files">Files</Nav.Link>
                                <Nav.Link href="/transit">Transfers</Nav.Link>
                                <Nav.Link href="/security">Security</Nav.Link>
                            </Nav>
                        </Offcanvas.Body>
                    </Navbar.Offcanvas>
                </Container>
            </Navbar>

            <Modal
                size="sm"
                show={showLogoutModal}
                aria-labelledby="example-modal-sizes-title-sm">
                <Modal.Body>Logging out...</Modal.Body>
            </Modal>
        </header>
    );
}

export default NavMenu;