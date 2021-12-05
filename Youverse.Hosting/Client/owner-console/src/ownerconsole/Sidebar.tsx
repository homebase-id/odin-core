import React, {useState} from 'react';
import {FontAwesomeIcon} from '@fortawesome/react-fontawesome';
import {faHouseUser, faGrinStars, faPeopleArrows, faFile, faClone, faDiceD20, faDragon, faBezierCurve, faBlog, faGlobe} from '@fortawesome/free-solid-svg-icons'
import {Link, LinkProps, NavLink, useMatch, useResolvedPath} from 'react-router-dom';
import {usePopper} from "react-popper";
import {OverlayTrigger, Popover, Tooltip} from "react-bootstrap";
import {OverlayChildren, OverlayInjectedProps} from "react-bootstrap/Overlay";
import {IconProp} from "@fortawesome/fontawesome-svg-core";

interface ActiveDetectLinkProps extends Omit<LinkProps, "children"> {
    popover: React.ReactElement,
    icon: IconProp
}

function ActiveDetectLink({to, icon, popover, ...props}: ActiveDetectLinkProps) {
    let resolved = useResolvedPath(to);
    let match = useMatch({path: resolved.pathname, end: true});

    let css = "nav-link py-3 border-bottom";
    css += match ? " active" : "";

    //const x: OverlayChildren = (props1: OverlayInjectedProps) => <Tooltip id="button-tooltip" {...props1}>{tooltip}</Tooltip>

    return (
        <OverlayTrigger
            placement="right"
            delay={{show: 250, hide: 400}}
            overlay={popover}>
            <li className="nav-item">
                <Link className={css} to={to} {...props}>
                    <FontAwesomeIcon icon={icon} size="2x"/>
                </Link>
            </li>
        </OverlayTrigger>
    );
}

function Sidebar(props: any) {
    return (
        <>
            <div className="-flex flex-column flex-shrink-0 bg-light">
                <a href="/" className="d-block p-3 link-dark text-decoration-none" title="" data-bs-toggle="tooltip" data-bs-placement="right" data-bs-original-title="Icon-only">
                    <FontAwesomeIcon icon={faDragon} size="2x"/>
                    <span className="visually-hidden">Youverse</span>
                </a>
                <ul className="nav nav-pills nav-flush flex-column mb-auto text-center">
                    <ActiveDetectLink
                        to="/owner/dashboard"
                        icon={faHouseUser}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">Home</Popover.Header>
                            <Popover.Body>See a high level view of things</Popover.Body>
                        </Popover>}/>

                    <ActiveDetectLink
                        to="/owner/mynetwork"
                        icon={faBezierCurve}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">My Network</Popover.Header>
                            <Popover.Body>
                                Manage your network - contacts, connections, friends, and associates
                            </Popover.Body>
                        </Popover>}/>

                    <ActiveDetectLink
                        to="/owner/apps"
                        icon={faDiceD20}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">Apps</Popover.Header>
                            <Popover.Body>
                                View, authorize, and reject apps
                            </Popover.Body>
                        </Popover>}/>

                    <ActiveDetectLink
                        to="/owner/transfer"
                        icon={faPeopleArrows}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">Transit</Popover.Header>
                            <Popover.Body>See all of your data transfers</Popover.Body>
                        </Popover>}/>

                    <ActiveDetectLink
                        to="/owner/landingpage"
                        icon={faGlobe}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">Landing page</Popover.Header>
                            <Popover.Body>Configure your landing page and public links</Popover.Body>
                        </Popover>}/>

                    <ActiveDetectLink
                        to="/owner/blog"
                        icon={faBlog}
                        popover={<Popover id="popover-basic">
                            <Popover.Header as="h3">Blog</Popover.Header>
                            <Popover.Body>Your blog</Popover.Body>
                        </Popover>}/>
                </ul>
                <div className="dropdown border-top">
                    <a href="#" className="d-flex align-items-center justify-content-center p-3 link-dark text-decoration-none dropdown-toggle" id="dropdownUser3" data-bs-toggle="dropdown"
                       aria-expanded="false">
                        <img src="https://github.com/mdo.png" alt="mdo" className="rounded-circle" width="24" height="24"/>
                    </a>
                    <ul className="dropdown-menu text-small shadow" aria-labelledby="dropdownUser3">
                        <li><a className="dropdown-item" href="#">New project...</a></li>
                        <li><a className="dropdown-item" href="#">Settings</a></li>
                        <li><a className="dropdown-item" href="#">Profile</a></li>
                        <li>
                            <hr className="dropdown-divider"/>
                        </li>
                        <li><a className="dropdown-item" href="#">Sign out</a></li>
                    </ul>
                </div>
            </div>
        </>
    );
}

export default Sidebar;

