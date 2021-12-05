import React from 'react';
import {FontAwesomeIcon} from '@fortawesome/react-fontawesome';
import {faHouseUser, faPeopleArrows, faDiceD20, faDragon, faBezierCurve, faBlog, faGlobe} from '@fortawesome/free-solid-svg-icons'
import {Link, LinkProps, useMatch, useResolvedPath} from 'react-router-dom';
import {OverlayTrigger, Popover} from "react-bootstrap";
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
        <div className="d-flex flex-column flex-shrink-0 bg-light">
            <a className="d-block p-3 link-dark text-decoration-none" title="" data-bs-toggle="tooltip" data-bs-placement="right" data-bs-original-title="Icon-only">
                <FontAwesomeIcon icon={faDragon} size="2x"/>
                <span className="visually-hidden">Youverse</span>
            </a>
            <ul className="nav nav-pills nav-flush flex-column mb-auto text-center">
                <ActiveDetectLink
                    to="/owner/dashboard"
                    icon={faHouseUser}
                    popover={<Popover id="popover-dashboard">
                        <Popover.Header as="h3">Home</Popover.Header>
                        <Popover.Body>See a high level view of things</Popover.Body>
                    </Popover>}/>

                <ActiveDetectLink
                    to="/owner/mynetwork"
                    icon={faBezierCurve}
                    popover={<Popover id="popover-mynetwork">
                        <Popover.Header as="h3">My Network</Popover.Header>
                        <Popover.Body>
                            Manage your network - contacts, connections, friends, and associates
                        </Popover.Body>
                    </Popover>}/>

                <ActiveDetectLink
                    to="/owner/apps"
                    icon={faDiceD20}
                    popover={<Popover id="popover-apps">
                        <Popover.Header as="h3">Apps</Popover.Header>
                        <Popover.Body>
                            View, authorize, and reject apps
                        </Popover.Body>
                    </Popover>}/>

                <ActiveDetectLink
                    to="/owner/transit"
                    icon={faPeopleArrows}
                    popover={<Popover id="popover-transit">
                        <Popover.Header as="h3">Transit</Popover.Header>
                        <Popover.Body>See all of your data transfers</Popover.Body>
                    </Popover>}/>

                <ActiveDetectLink
                    to="/owner/landingpage"
                    icon={faGlobe}
                    popover={<Popover id="popover-lp">
                        <Popover.Header as="h3">Landing page</Popover.Header>
                        <Popover.Body>Configure your landing page and public links</Popover.Body>
                    </Popover>}/>

                <ActiveDetectLink
                    to="/owner/blog"
                    icon={faBlog}
                    popover={<Popover id="popover-blog">
                        <Popover.Header as="h3">Blog</Popover.Header>
                        <Popover.Body>Share your ideas with the world</Popover.Body>
                    </Popover>}/>
            </ul>
        </div>
    );
}

export default Sidebar;

