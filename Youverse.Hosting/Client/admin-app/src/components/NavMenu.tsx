import React, {useState} from 'react';
import './NavMenu.css';
import {Container, Navbar, Nav, Modal, NavDropdown, DropdownButton} from "react-bootstrap";
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
                            menuAlign="left"
                            title=""
                            id="meMenu">
                            <Nav.Link as={Link} to="/profile" className="text-dark nav-link">My Profile</Nav.Link>
                            <Nav.Link as={Link} to="/privacy" className="text-dark nav-link text-nowrap">Settings and Privacy</Nav.Link>
                            <NavDropdown.Divider/>
                            <a href="#" onClick={handleLogout} className="text-dark nav-link">Logout</a>
                        </DropdownButton>
                    </Navbar.Collapse>
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