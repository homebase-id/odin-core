import React, {Component} from "react";
import {Navbar, Container, Nav, Dropdown, Button, Offcanvas, NavDropdown, Form, FormControl} from "react-bootstrap";
import {FontAwesomeIcon} from '@fortawesome/react-fontawesome';
import {faHouseUser, faPeopleArrows, faDiceD20, faDragon, faBezierCurve, faBlog, faGlobe, faUser, faUserAlt, faUserNinja, faChevronCircleDown} from '@fortawesome/free-solid-svg-icons'
import Sidebar from "./Sidebar";

interface IHeaderNavbarProps {
    children?: React.ReactElement;
}

function HeaderNavbar(props: IHeaderNavbarProps) {
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

    // const avatar = <img src="https://github.com/mdo.png" alt="mdo" className="rounded-circle" width="32" height="32"/>;
    const avatar = <FontAwesomeIcon icon={faUserNinja} size="lg"/>;
    const notificationsIcon = <FontAwesomeIcon icon={faDragon} size="lg"/>;
    const contextMenuIcon = <FontAwesomeIcon icon={faChevronCircleDown} size="lg"/>

    return (
        <header className="bg-dark text-white">
            <Navbar bg="light" variant="light" expand="md">
                <Container fluid={true} className="justify-content-between d-flex flex-row flex-grow-1">
                    {/*{props.children}*/}
                    <ul className="nav justify-content-center d-none d-md-flex">
                        <li><a href="#" className="nav-link px-2 link-secondary">Context menu 1</a></li>
                        <li><a href="#" className="nav-link px-2 link-dark">Context menu 2</a></li>
                    </ul>

                    <ul className="nav justify-content-center d-flex d-md-none">
                        <li><Navbar.Toggle aria-controls="offcanvasNavbar"/></li>
                        <li>
                            <NavDropdown title={contextMenuIcon} align="start" id="navbarScrollingDropdown">
                                <NavDropdown.Item href="#action3">Context menu 1</NavDropdown.Item>
                                <NavDropdown.Item href="#action4">Context menu 2</NavDropdown.Item>
                            </NavDropdown>
                        </li>
                    </ul>

                    <ul className="nav justify-content-center">
                        <li>
                            <NavDropdown title={notificationsIcon} align="end" id="navbarScrollingDropdown">
                                <NavDropdown.Item href="#action3">Action</NavDropdown.Item>
                                <NavDropdown.Item href="#action4">Another action</NavDropdown.Item>
                                <NavDropdown.Divider/>
                                <NavDropdown.Item href="#action5">
                                    <FontAwesomeIcon icon={faDragon} size="10x"/>
                                </NavDropdown.Item>
                            </NavDropdown>
                        </li>
                        <li>
                            <NavDropdown title={avatar} align="end" id="navbarScrollingDropdown">
                                <NavDropdown.Item href="#action3">Action</NavDropdown.Item>
                                <NavDropdown.Item href="#action4">Another action</NavDropdown.Item>
                                <NavDropdown.Divider/>
                                <NavDropdown.Item href="#action5">
                                    Something else here
                                </NavDropdown.Item>
                            </NavDropdown>
                        </li>
                    </ul>

                </Container>
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
            </Navbar>
        </header>
    );
}

export default HeaderNavbar;
