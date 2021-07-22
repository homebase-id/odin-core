import React, {Component, useState} from 'react';
import './NavMenu.css';
import {Collapse, Container, Navbar, NavItem, Nav, Modal} from "react-bootstrap";
import {Link} from "react-router-dom";
import {useAppStateStore} from "../provider/AppStateStore";

interface IState {
    collapsed: boolean,
    showLogoutModal: boolean
}

function NavMenu(props: any) {

    const [collapsed, setCollapsed] = useState<boolean>(false);
    const [showLogoutModal, setShowLogoutModal] = useState<boolean>(false);
    const {logout} = useAppStateStore();

    const toggleNavbar = () => {
        setCollapsed(!collapsed);
    }

    const handleLogout = () => {
        console.log('pie');
        setShowLogoutModal(true);
        logout().then(success => {
            if (success) {
                window.location.href = "/?lo=1"
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
                        <ul className="navbar-nav flex-grow">
                            <NavItem>
                                <Nav.Link as={Link} to="/" className="text-dark">Home</Nav.Link>
                            </NavItem>
                            <NavItem>
                                <a href="#" onClick={handleLogout} className="text-dark nav-link">Logout</a>
                                {/*<Nav.Link onSelect={handleLogout} as={Link} to="/" className="text-dark">Logout</Nav.Link>*/}
                            </NavItem>
                        </ul>
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