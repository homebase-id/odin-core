import React, {useState} from 'react';
import './NavMenu.css';
import {Container, Navbar, Nav, Modal, NavDropdown, DropdownButton, Offcanvas, Form, Button, FormControl} from "react-bootstrap";
import {Link} from "react-router-dom";
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
            <Navbar className="navbar-expand-sm navbar-toggleable-sm ng-white border-bottom box-shadow mb-3" bg="light">
                <Container>
                    <Navbar.Brand href="/">DotYou</Navbar.Brand>
                    <Navbar.Toggle onClick={toggleNavbar} className="mr-2"/>
                    <Navbar.Collapse className="d-sm-inline-flex flex-sm-row-reverse" in={!collapsed}>
                        <DropdownButton
                            title=""
                            id="meMenu">
                            {/*<Nav.Link as={Link} to="/profile" className="text-dark nav-link">My Profile</Nav.Link>*/}
                            {/*<Nav.Link as={Link} to="/privacy" className="text-dark nav-link text-nowrap">Settings and Privacy</Nav.Link>*/}
                            <NavDropdown.Divider/>
                            <a href="#" onClick={handleLogout} className="text-dark nav-link">Logout</a>
                        </DropdownButton>
                    </Navbar.Collapse>
                </Container>
            </Navbar>

            <Navbar bg="light" expand={false}>
                <Container fluid>
                    <Navbar.Brand href="#">Youverse Admin</Navbar.Brand>
                    <Navbar.Toggle aria-controls="offcanvasNavbar"/>
                    <Navbar.Offcanvas
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