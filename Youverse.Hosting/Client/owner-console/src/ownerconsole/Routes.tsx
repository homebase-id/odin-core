import Dashboard from "./Dashboard";
import {Route} from "react-router-dom";
export class RouteDefinition {
    path:string = "";
    title:string = "";
    icon:string = "";
    component: React.ReactElement | null = null;
}

const routes: RouteDefinition[] = [

    {
        path: "/admin",
        title: "Dashboard",
        icon: "",
        component: <Dashboard />
    },
    
]

export const renderRoutes = (routes: RouteDefinition[]) => {
    return routes.map((prop:RouteDefinition, key:any) => {
        return (<Route path={prop.path} element={prop.component} key={key} />); 
    });
  };

export default routes;