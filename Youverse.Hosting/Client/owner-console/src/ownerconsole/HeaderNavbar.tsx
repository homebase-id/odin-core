import React, {Component} from "react";
import {Navbar, Container, Nav, Dropdown, Button, Offcanvas, NavDropdown, Form, FormControl} from "react-bootstrap";
import {FontAwesomeIcon} from '@fortawesome/react-fontawesome';
import {faHouseUser, faPeopleArrows, faDiceD20, faDragon, faBezierCurve, faBlog, faGlobe, faUser, faUserAlt} from '@fortawesome/free-solid-svg-icons'
import Sidebar from "./Sidebar";

interface IHeaderNavbarProps
{
    children?:React.ReactElement;
}

function HeaderNavbar(props:IHeaderNavbarProps) {
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
        <header className="p-3 mb-3 border-bottom">
            <Container>
                <div className="d-flex flex-wrap align-items-center justify-content-center justify-content-lg-start">
                    <FontAwesomeIcon icon={faDragon} size="lg" />

                    {props.children}

                    <form className="col-2 me-lg-3">
                        <input type="search" className="form-control" placeholder="Search..." aria-label="Search"/>
                    </form>
                    
                    <NavDropdown title={<img src="https://github.com/mdo.png" alt="mdo" className="rounded-circle" width="32" height="32" />} id="basic-nav-dropdown">
                        <NavDropdown.Item href="#action/3.1">Action</NavDropdown.Item>
                        <NavDropdown.Item href="#action/3.2">Another action</NavDropdown.Item>
                        <NavDropdown.Item href="#action/3.3">Something</NavDropdown.Item>
                        <NavDropdown.Divider />
                        <NavDropdown.Item href="#action/3.4">Separated link</NavDropdown.Item>
                    </NavDropdown>
                </div>
            </Container>
        </header>
    );
}

export default HeaderNavbar;

{/* 
<header className="p-3 bg-dark text-white">
            <Navbar bg="dark" variant="dark" expand="md">
                <Container fluid>
                    <Navbar.Toggle aria-controls="offcanvasNavbar" />

                        <Nav>
                            Oage
                        </Nav>
                        <Nav className="flex-row flex-nowrap justify-content-between">
                            <Nav.Item className="mr-3">
                                <Nav.Link href="#deets">
                                    <FontAwesomeIcon icon={faDragon} size="lg" />
                                </Nav.Link>
                            </Nav.Item>
                            <Nav.Item>
                                <Nav.Link eventKey={2} href="#memes">
                                    <FontAwesomeIcon icon={faUserAlt} size="lg" />
                                </Nav.Link>
                            </Nav.Item>

                        </Nav>
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
        </header> */}