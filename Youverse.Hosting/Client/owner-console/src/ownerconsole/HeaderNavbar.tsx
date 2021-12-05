import React, {Component} from "react";
import {useLocation} from "react-router-dom";
import {Navbar, Container, Nav, Dropdown, Button, Offcanvas, NavDropdown, Form, FormControl} from "react-bootstrap";
import Sidebar from "./Sidebar";

function HeaderNavbar() {
    // const location = useLocation();
    const mobileSidebarToggle = (e: any) => {
        // e.preventDefault();
        // document.documentElement.classList.toggle("nav-open");
        // var node = document.createElement("div");
        // node.id = "bodyClick";
        // node.onclick = function () {
        //   this.parentElement.removeChild(this);
        //   document.documentElement.classList.toggle("nav-open");
        // };
        // document.body.appendChild(node);
    };

    const getHeaderText = () => {
        return "Dashboard";
    };

    return (
        <header className="p-3 bg-dark text-white">
            <Navbar bg="dark" variant="dark" expand="md">
                <Container fluid>
                    <Navbar.Toggle aria-controls="offcanvasNavbar" />
                    <Navbar.Brand href="#">Navbar Offcanvas</Navbar.Brand>
                    <Navbar.Offcanvas
                        id="offcanvasNavbar"
                        aria-labelledby="offcanvasNavbarLabel"
                        placement="start"
                        className="offcanvas-menu">
                        <Offcanvas.Header closeButton>
                            <Offcanvas.Title id="offcanvasNavbarLabel"></Offcanvas.Title>
                        </Offcanvas.Header>
                        <Offcanvas.Body>
                           <Sidebar/>
                        </Offcanvas.Body>
                    </Navbar.Offcanvas>
                </Container>
            </Navbar>

        </header>
    );
}

export default HeaderNavbar;
