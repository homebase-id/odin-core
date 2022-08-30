"use strict";(self.webpackChunkowner_app=self.webpackChunkowner_app||[]).push([[864],{2723:function(e,t,r){r.d(t,{Z:function(){return p}});var n=r(1413),a=r(885),i=r(1803),s=r(2791),l=r(4990),o=r(4164),c=r(3412),u=r(8191),d=r(184),m=function(e){return(0,d.jsx)("input",(0,n.Z)((0,n.Z)({},e),{},{type:"radio",className:"h-4 w-4 rounded-full border-gray-300 bg-gray-100 text-blue-600 focus:outline-none dark:border-gray-600 dark:bg-gray-700 dark:ring-offset-gray-800"}))},f=function(e){var t=e.title,r=e.confirmText,f=e.isOpen,x=e.acl,h=e.onConfirm,g=e.onCancel,v=(0,c.Z)("modal-container"),p=(0,s.useState)(x),b=(0,a.Z)(p,2),j=b[0],y=b[1];if(!f)return null;var C=function(e){y((0,n.Z)((0,n.Z)({},x),{},{requiredSecurityGroup:i.hh[e.target.value]}))},w=(0,d.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,d.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,d.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,d.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,d.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",children:[(0,d.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black dark:text-white sm:p-6 sm:pb-4",children:(0,d.jsx)("div",{className:"sm:flex sm:items-start",children:(0,d.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,d.jsx)("h3",{className:"mb-3 text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"anonymous",value:"Anonymous",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Anonymous,onChange:C}),(0,d.jsx)("label",{htmlFor:"anonymous",className:"ml-2",children:"Anonymous"})]}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"authenticated",value:"Authenticated",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Authenticated,onChange:C}),(0,d.jsx)("label",{htmlFor:"authenticated",className:"ml-2",children:"Authenticated"})]}),(0,d.jsxs)("div",{children:[(0,d.jsx)(m,{id:"connected",value:"Connected",name:"securityGroup",checked:j.requiredSecurityGroup===i.hh.Connected,onChange:C}),(0,d.jsx)("label",{htmlFor:"connected",className:"ml-2",children:"Connected"})]})]})})}),(0,d.jsxs)("div",{className:"-mx-2 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,d.jsx)(u.Z,{className:"mx-2",onClick:function(){h(j)},children:null!==r&&void 0!==r?r:"Save"}),(0,d.jsx)(u.Z,{className:"mx-2",type:"secondary",onClick:g,children:(0,l.t)("Cancel")})]})]})})})]});return(0,o.createPortal)(w,v)},x=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 448 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M377.7 338.8l37.15-92.87C419 235.4 411.3 224 399.1 224h-57.48C348.5 209.2 352 193 352 176c0-4.117-.8359-8.057-1.217-12.08C390.7 155.1 416 142.3 416 128c0-16.08-31.75-30.28-80.31-38.99C323.8 45.15 304.9 0 277.4 0c-10.38 0-19.62 4.5-27.38 10.5c-15.25 11.88-36.75 11.88-52 0C190.3 4.5 181.1 0 170.7 0C143.2 0 124.4 45.16 112.5 88.98C63.83 97.68 32 111.9 32 128c0 14.34 25.31 27.13 65.22 35.92C96.84 167.9 96 171.9 96 176C96 193 99.47 209.2 105.5 224H48.02C36.7 224 28.96 235.4 33.16 245.9l37.15 92.87C27.87 370.4 0 420.4 0 477.3C0 496.5 15.52 512 34.66 512H413.3C432.5 512 448 496.5 448 477.3C448 420.4 420.1 370.4 377.7 338.8zM176 479.1L128 288l64 32l16 32L176 479.1zM271.1 479.1L240 352l16-32l64-32L271.1 479.1zM320 186C320 207 302.8 224 281.6 224h-12.33c-16.46 0-30.29-10.39-35.63-24.99C232.1 194.9 228.4 192 224 192S215.9 194.9 214.4 199C209 213.6 195.2 224 178.8 224h-12.33C145.2 224 128 207 128 186V169.5C156.3 173.6 188.1 176 224 176s67.74-2.383 96-6.473V186z"})})},h=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 576 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M96 304.1c0-12.16 4.971-23.83 13.64-32.01l72.13-68.08c1.65-1.555 3.773-2.311 5.611-3.578C177.1 176.8 155 160 128 160H64C28.65 160 0 188.7 0 224v96c0 17.67 14.33 32 31.1 32L32 480c0 17.67 14.33 32 32 32h64c17.67 0 32-14.33 32-32v-96.39l-50.36-47.53C100.1 327.9 96 316.2 96 304.1zM480 128c35.38 0 64-28.62 64-64s-28.62-64-64-64s-64 28.62-64 64S444.6 128 480 128zM96 128c35.38 0 64-28.62 64-64S131.4 0 96 0S32 28.62 32 64S60.63 128 96 128zM444.4 295.3L372.3 227.3c-3.49-3.293-8.607-4.193-13.01-2.299C354.9 226.9 352 231.2 352 236V272H224V236c0-4.795-2.857-9.133-7.262-11.03C212.3 223.1 207.2 223.1 203.7 227.3L131.6 295.3c-4.805 4.535-4.805 12.94 0 17.47l72.12 68.07c3.49 3.291 8.607 4.191 13.01 2.297C221.1 381.3 224 376.9 224 372.1V336h128v36.14c0 4.795 2.857 9.135 7.262 11.04c4.406 1.893 9.523 .9922 13.01-2.299l72.12-68.07C449.2 308.3 449.2 299.9 444.4 295.3zM512 160h-64c-26.1 0-49.98 16.77-59.38 40.42c1.842 1.271 3.969 2.027 5.623 3.588l72.12 68.06C475 280.2 480 291.9 480 304.1c.002 12.16-4.969 23.83-13.64 32.01L416 383.6V480c0 17.67 14.33 32 32 32h64c17.67 0 32-14.33 32-32v-128c17.67 0 32-14.33 32-32V224C576 188.7 547.3 160 512 160z"})})},g=function(e){var t=e.className;return(0,d.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,d.jsx)("path",{d:"M352 256C352 278.2 350.8 299.6 348.7 320H163.3C161.2 299.6 159.1 278.2 159.1 256C159.1 233.8 161.2 212.4 163.3 192H348.7C350.8 212.4 352 233.8 352 256zM503.9 192C509.2 212.5 512 233.9 512 256C512 278.1 509.2 299.5 503.9 320H380.8C382.9 299.4 384 277.1 384 256C384 234 382.9 212.6 380.8 192H503.9zM493.4 160H376.7C366.7 96.14 346.9 42.62 321.4 8.442C399.8 29.09 463.4 85.94 493.4 160zM344.3 160H167.7C173.8 123.6 183.2 91.38 194.7 65.35C205.2 41.74 216.9 24.61 228.2 13.81C239.4 3.178 248.7 0 256 0C263.3 0 272.6 3.178 283.8 13.81C295.1 24.61 306.8 41.74 317.3 65.35C328.8 91.38 338.2 123.6 344.3 160H344.3zM18.61 160C48.59 85.94 112.2 29.09 190.6 8.442C165.1 42.62 145.3 96.14 135.3 160H18.61zM131.2 192C129.1 212.6 127.1 234 127.1 256C127.1 277.1 129.1 299.4 131.2 320H8.065C2.8 299.5 0 278.1 0 256C0 233.9 2.8 212.5 8.065 192H131.2zM194.7 446.6C183.2 420.6 173.8 388.4 167.7 352H344.3C338.2 388.4 328.8 420.6 317.3 446.6C306.8 470.3 295.1 487.4 283.8 498.2C272.6 508.8 263.3 512 255.1 512C248.7 512 239.4 508.8 228.2 498.2C216.9 487.4 205.2 470.3 194.7 446.6H194.7zM190.6 503.6C112.2 482.9 48.59 426.1 18.61 352H135.3C145.3 415.9 165.1 469.4 190.6 503.6V503.6zM321.4 503.6C346.9 469.4 366.7 415.9 376.7 352H493.4C463.4 426.1 399.8 482.9 321.4 503.6V503.6z"})})},v=r(6910),p=function(e){var t=e.acl,r=e.onChange,o=(0,s.useState)(!1),c=(0,a.Z)(o,2),u=c[0],m=c[1],p=function(e){return t.requiredSecurityGroup===i.hh.Anonymous?(0,d.jsx)(g,(0,n.Z)({},e)):t.requiredSecurityGroup===i.hh.Authenticated?(0,d.jsx)(x,(0,n.Z)({},e)):t.requiredSecurityGroup===i.hh.Connected?(0,d.jsx)(h,(0,n.Z)({},e)):(t.requiredSecurityGroup,i.hh.Owner,(0,d.jsx)(v.Z,(0,n.Z)({},e)))};return(0,d.jsxs)(d.Fragment,{children:[(0,d.jsx)("button",{title:t.requiredSecurityGroup,className:"mr-2 inline-block ".concat(r?"":"cursor-default"),onClick:function(){return r&&m(!0)},children:(0,d.jsx)(p,{className:"h-5 w-5"})}),(0,d.jsx)(f,{acl:t,isOpen:u,title:(0,l.t)("Edit security"),onCancel:function(){m(!1)},onConfirm:function(e){r(e),m(!1)}})]})}},4226:function(e,t,r){var n=r(1803),a=r(2878),i=r(2791),s=r(4990),l=r(977),o=r(6916),c=r(3225),u=r(2465),d=r(184);t.Z=function(e){var t=e.attribute,r=e.onChange,m=(0,i.useMemo)((function(){return(0,a.Z)(r,500)}),[r]),f=Object.keys(n.Qv).filter((function(e){return!isNaN(Number(e))})),x=Object.keys(n.Qv).filter((function(e){return isNaN(Number(e))}));switch(t.type){case n.H1.Name.type.toString():return(0,d.jsxs)("div",{className:"-mx-2 flex flex-row",children:[(0,d.jsxs)("div",{className:"mb-5 w-2/5 px-2",children:[(0,d.jsx)("label",{htmlFor:"given-name",children:(0,s.t)("Given name")}),(0,d.jsx)(o.Z,{id:"given-name",name:"givenName",defaultValue:t.data.givenName,onChange:m})]}),(0,d.jsxs)("div",{className:"mb-5 w-3/5 px-2",children:[(0,d.jsx)("label",{htmlFor:"sur-name",children:(0,s.t)("Surname")}),(0,d.jsx)(o.Z,{id:"sur-name",name:"surname",defaultValue:t.data.surname,onChange:m})]})]});case n.H1.Photo.type.toString():return(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"profileImageId",children:(0,s.t)("Profile Image")}),(0,d.jsx)(l.Z,{id:"profileImageId",name:"profileImageId",defaultValue:t.data.profileImageId,onChange:m,acl:t.acl,targetDrive:(0,n.H3)(n.P.StandardProfileId.toString())})]});case n.H1.InstagramUsername.type.toString():case n.H1.TiktokUsername.type.toString():case n.H1.TwitterUsername.type.toString():case n.H1.LinkedInUsername.type.toString():case n.H1.FacebookUsername.type.toString():return(0,d.jsx)(d.Fragment,{children:(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"handle",children:t.typeDefinition.name}),(0,d.jsx)(o.Z,{id:"handle",name:t.typeDefinition.name.toLowerCase(),defaultValue:t.data[t.typeDefinition.name.toLowerCase()],onChange:m})]})});case n.H1.FullBio.type.toString():return(0,d.jsxs)(d.Fragment,{children:[(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"short-bio",children:(0,s.t)("Bio")}),(0,d.jsx)(o.Z,{id:"short-bio",name:"short_bio",defaultValue:t.data.short_bio,onChange:m})]}),(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"full-bio",children:(0,s.t)("Full bio")}),(0,d.jsx)(u.Z,{id:"full-bio",name:"full_bio",defaultValue:t.data.full_bio,onChange:m})]})]});case n.H1.ShortBio.type.toString():return(0,d.jsx)(d.Fragment,{children:(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"short-bio",children:(0,s.t)("Bio")}),(0,d.jsx)(o.Z,{id:"short-bio",name:"short_bio",defaultValue:t.data.short_bio,onChange:m})]})});case n.H1.CreditCard.type.toString():return(0,d.jsxs)(d.Fragment,{children:[(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"cc-alias",children:(0,s.t)("Alias")}),(0,d.jsx)(o.Z,{id:"cc-alias",name:"cc_alias",defaultValue:t.data.cc_alias,onChange:m})]}),(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"cc-name",children:(0,s.t)("Name on Card")}),(0,d.jsx)(o.Z,{id:"cc-name",name:"cc_name",defaultValue:t.data.cc_name,onChange:m})]}),(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"cc-number",children:(0,s.t)("Credit card number")}),(0,d.jsx)(o.Z,{id:"cc-number",name:"cc_number",defaultValue:t.data.cc_number,onChange:m})]})]});case n.Dg.HomePage.toString():return(0,d.jsxs)(d.Fragment,{children:[(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"headerImageUrl",children:(0,s.t)("Header Image")}),(0,d.jsx)(l.Z,{id:"headerImageUrl",name:"headerImageUrl",defaultValue:t.data.headerImageUrl,onChange:m,acl:t.acl,targetDrive:(0,n.H3)(n.Ib.DefaultDriveId.toString())})]}),(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"tagLine",children:(0,s.t)("Tag Line")}),(0,d.jsx)(o.Z,{id:"tagLine",name:"tagLine",defaultValue:t.data.tagLine,onChange:m})]}),(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"leadText",children:(0,s.t)("Lead Text")}),(0,d.jsx)(u.Z,{id:"leadText",name:"leadText",defaultValue:t.data.leadText,onChange:m})]})]});case n.Dg.Theme.toString():return(0,d.jsx)(d.Fragment,{children:(0,d.jsxs)("div",{className:"mb-5",children:[(0,d.jsx)("label",{htmlFor:"themeId",children:(0,s.t)("Theme")}),(0,d.jsxs)(c.Z,{id:"themeId",name:"themeId",defaultValue:t.data.themeId,onChange:m,children:[(0,d.jsx)("option",{children:(0,s.t)("Make a selection")}),f.map((function(e,t){return(0,d.jsx)("option",{value:e,children:(0,s.t)(x[t])},e)}))]})]})});default:return(0,d.jsx)(d.Fragment,{children:Object.keys(t.data).map((function(e){return(0,d.jsxs)("p",{className:"whitespace-pre-line",children:[e,": ",t.data[e]]},e)}))})}}},2864:function(e,t,r){r.d(t,{Z:function(){return v}});var n=r(885),a=r(2791),i=r(184),s=function(e){var t=e.className;return(0,i.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,i.jsx)("path",{d:"M215.1 272h-136c-12.94 0-24.63 7.797-29.56 19.75C45.47 303.7 48.22 317.5 57.37 326.6l30.06 30.06l-78.06 78.07c-12.5 12.5-12.5 32.75-.0012 45.25l22.62 22.62c12.5 12.5 32.76 12.5 45.26 .0013l78.06-78.07l30.06 30.06c6.125 6.125 14.31 9.367 22.63 9.367c4.125 0 8.279-.7891 12.25-2.43c11.97-4.953 19.75-16.62 19.75-29.56V296C239.1 282.7 229.3 272 215.1 272zM296 240h136c12.94 0 24.63-7.797 29.56-19.75c4.969-11.97 2.219-25.72-6.938-34.87l-30.06-30.06l78.06-78.07c12.5-12.5 12.5-32.76 .0002-45.26l-22.62-22.62c-12.5-12.5-32.76-12.5-45.26-.0003l-78.06 78.07l-30.06-30.06c-9.156-9.141-22.87-11.84-34.87-6.937c-11.97 4.953-19.75 16.62-19.75 29.56v135.1C272 229.3 282.7 240 296 240z"})})},l=r(1413),o=r(4990),c=r(9077),u=r(8191),d=r(5660),m=r(8923),f=r(8491),x=r(2723),h=r(4226),g=function(e){var t=e.attribute,r=e.className,s=(0,a.useState)(!1),g=(0,n.Z)(s,2),v=g[0],p=g[1],b=(0,c.Z)({}),j=(0,n.Z)(b,3),y=j[1],C=y.mutate,w=y.status,N=y.isLoading,Z=y.isError,k=y.isSuccess,S=j[2].mutate,I=(0,a.useState)((0,l.Z)({},t)),O=(0,n.Z)(I,2),D=O[0],F=O[1],H=function(e){C(e)},L=function(e){var t=(0,l.Z)({},D);t[e.target.name]=e.target.value,F(t),H(t)};return(0,i.jsxs)(i.Fragment,{children:[(0,i.jsxs)(f.Z,{isOpaqueBg:!0,title:(0,i.jsxs)(i.Fragment,{children:[(0,i.jsx)(x.Z,{acl:D.acl,onChange:function(e){L({target:{name:"acl",value:e}})}})," ",D.typeDefinition.name]}),className:"".concat(r," relative"),children:[(0,i.jsx)(h.Z,{attribute:D,onChange:L}),(0,i.jsxs)("div",{className:"top-5 right-5 flex flex-row md:absolute",children:[(0,i.jsx)(u.Z,{type:"remove",icon:"trash",className:"ml-auto",onClick:function(){p(!0)}}),(0,i.jsx)(u.Z,{state:N?"loading":k?"success":Z?"failed":"success",type:"primary",className:"ml-2",onClick:function(){return H(D)},children:(0,o.t)("Save")})]}),(0,i.jsx)(d.Z,{className:"mt-2 text-right sm:mt-0",state:w})]}),(0,i.jsx)(m.Z,{title:"Remove Attribute",confirmText:"Permanently remove",needConfirmation:v,onConfirm:function(){p(!1),S(t)},onCancel:function(){p(!1)},children:(0,i.jsxs)("p",{className:"text-sm text-gray-500",children:[(0,o.t)("Are you sure you want to remove your")," ",t.typeDefinition.name," ",(0,o.t)("attribute. This action cannot be undone.")]})})]})},v=function(e){var t=e.attributes,r=e.groupTitle,l=(0,a.useState)(1===t.length),o=(0,n.Z)(l,2),c=o[0],u=o[1];return 1===t.length?(0,i.jsx)(g,{attribute:t[0]}):(0,i.jsxs)("div",{className:"relative my-10 overflow-x-hidden ".concat(c?"":"cursor-pointer transition-transform"),style:{paddingBottom:"".concat(10*t.length,"px")},onClick:function(){c||u(!0)},children:[(0,i.jsxs)("h2",{onClick:function(){return u(!1)},className:"cursor-pointer text-2xl",children:[(0,i.jsx)(s,{className:"inline-block h-4 w-4 ".concat(c?"opacity-100":"opacity-0")})," ",r," ",(0,i.jsxs)("small",{children:["(",t.length,")"]})]}),(0,i.jsx)("div",{className:"border-l-[16px] border-slate-50 pt-5 transition-transform dark:border-slate-600 ".concat(c?"pl-5":"-translate-x-4 hover:translate-x-0"),children:t.map((function(e,t){return(0,i.jsx)("span",{className:c||0===t?"":"absolute left-0 right-0 top-0 bg-white shadow-slate-50 dark:bg-slate-800",style:{transform:"translateX(".concat(4*t,"px) translateY(").concat(10*t,"px)")},children:(0,i.jsx)(g,{attribute:e,className:c?"mt-0 mb-5":"pointer-events-none my-0 opacity-50 grayscale"})},e.id)}))})]})}},8923:function(e,t,r){var n=r(4164),a=r(4990),i=r(3412),s=r(1088),l=r(184);t.Z=function(e){var t=e.title,r=e.confirmText,o=e.children,c=e.needConfirmation,u=e.onConfirm,d=e.onCancel,m=(0,i.Z)("modal-container");if(!c)return null;var f=(0,l.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,l.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,l.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",onClick:d,children:(0,l.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,l.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",onClick:function(e){return e.preventDefault(),!1},children:[(0,l.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,l.jsxs)("div",{className:"sm:flex sm:items-start",children:[(0,l.jsx)("div",{className:"mx-auto flex h-12 w-12 flex-shrink-0 items-center justify-center rounded-full text-red-400 sm:mx-0 sm:h-10 sm:w-10",children:(0,l.jsx)(s.Z,{})}),(0,l.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,l.jsx)("h3",{className:"text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,l.jsx)("div",{className:"mt-2",children:o})]})]})}),(0,l.jsxs)("div",{className:"bg-gray-50 px-4 py-3 dark:bg-slate-900 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,l.jsx)("button",{type:"button",className:"inline-flex w-full justify-center rounded-md border border-transparent bg-red-600 px-4 py-2 text-base font-medium text-white shadow-sm hover:bg-red-700 focus:outline-none focus:ring-2 focus:ring-red-500 focus:ring-offset-2 sm:ml-3 sm:w-auto sm:text-sm",onClick:u,children:r}),(0,l.jsx)("button",{type:"button",className:"mt-3 inline-flex w-full justify-center rounded-md border border-gray-300 bg-white px-4 py-2 text-base font-medium text-gray-700 shadow-sm hover:bg-gray-50 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:ring-offset-2 dark:border-gray-800 dark:bg-slate-700 dark:text-white sm:mt-0 sm:ml-3 sm:w-auto sm:text-sm",onClick:d,children:(0,a.t)("Cancel")})]})]})})})]});return(0,n.createPortal)(f,m)}},6691:function(e,t,r){var n=r(4165),a=r(5861),i=r(885),s=r(2791),l=r(4164),o=r(4990),c=r(8088),u=r(3412),d=r(8191),m=r(184);t.Z=function(e){var t=e.title,r=e.confirmText,f=e.isOpen,x=e.acl,h=e.targetDrive,g=e.onConfirm,v=e.onCancel,p=(0,u.Z)("modal-container"),b=(0,c.Z)(),j=(0,i.Z)(b,2)[1],y=j.mutate,C=j.status,w=(0,s.useState)(),N=(0,i.Z)(w,2),Z=N[0],k=N[1];if(!f)return null;var S=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(){var t;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.t0=Uint8Array,e.next=3,Z.arrayBuffer();case 3:e.t1=e.sent,t=new e.t0(e.t1),y({acl:x,bytes:t,fileId:void 0,targetDrive:h},{onSuccess:g});case 6:case"end":return e.stop()}}),e)})));return function(){return e.apply(this,arguments)}}(),I=(0,m.jsxs)("div",{className:"relative z-50","aria-labelledby":"modal-title",role:"dialog","aria-modal":"true",children:[(0,m.jsx)("div",{className:"fixed inset-0 bg-gray-500 bg-opacity-75 transition-opacity"}),(0,m.jsx)("div",{className:"fixed inset-0 z-10 overflow-y-auto",children:(0,m.jsx)("div",{className:"flex min-h-full items-end justify-center p-4 text-center sm:items-center sm:p-0",children:(0,m.jsxs)("div",{className:"relative transform overflow-hidden rounded-lg bg-white text-left shadow-xl transition-all dark:bg-black sm:my-8 sm:w-full sm:max-w-lg",children:[(0,m.jsx)("div",{className:"bg-white px-4 pt-5 pb-4 dark:bg-black sm:p-6 sm:pb-4",children:(0,m.jsx)("div",{className:"sm:flex sm:items-start",children:(0,m.jsxs)("div",{className:"mt-3 text-center sm:mt-0 sm:ml-4 sm:text-left",children:[(0,m.jsx)("h3",{className:"mb-3 text-lg font-medium leading-6 text-gray-900 dark:text-slate-50",id:"modal-title",children:t}),(0,m.jsx)("input",{onChange:function(e){var t=e.target.files[0];t&&k(t)},type:"file",accept:"image/png, image/jpeg, image/tiff, image/webp",className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100"})]})})}),(0,m.jsxs)("div",{className:"-mx-2 px-4 py-3 sm:flex sm:flex-row-reverse sm:px-6",children:[(0,m.jsx)(d.Z,{className:"mx-2",onClick:S,state:C,children:null!==r&&void 0!==r?r:"Add"}),(0,m.jsx)(d.Z,{className:"mx-2",type:"secondary",onClick:v,children:(0,o.t)("Cancel")})]})]})})})]});return(0,l.createPortal)(I,p)}},977:function(e,t,r){r.d(t,{Z:function(){return f}});var n=r(885),a=r(2791),i=r(4990),s=r(8088),l=r(8923),o=r(1088),c=r(184),u=function(e){var t=e.className;return(0,c.jsx)("svg",{xmlns:"http://www.w3.org/2000/svg",viewBox:"0 0 512 512",fill:"currentColor",className:t,children:(0,c.jsx)("path",{d:"M421.7 220.3L188.5 453.4L154.6 419.5L158.1 416H112C103.2 416 96 408.8 96 400V353.9L92.51 357.4C87.78 362.2 84.31 368 82.42 374.4L59.44 452.6L137.6 429.6C143.1 427.7 149.8 424.2 154.6 419.5L188.5 453.4C178.1 463.8 165.2 471.5 151.1 475.6L30.77 511C22.35 513.5 13.24 511.2 7.03 504.1C.8198 498.8-1.502 489.7 .976 481.2L36.37 360.9C40.53 346.8 48.16 333.9 58.57 323.5L291.7 90.34L421.7 220.3zM492.7 58.75C517.7 83.74 517.7 124.3 492.7 149.3L444.3 197.7L314.3 67.72L362.7 19.32C387.7-5.678 428.3-5.678 453.3 19.32L492.7 58.75z"})})},d=r(7134),m=r(6691),f=function(e){var t=e.targetDrive,r=e.acl,f=e.onChange,x=e.defaultValue,h=e.name,g=(0,s.Z)("string"===typeof x?x:void 0,t),v=(0,n.Z)(g,3),p=v[0],b=p.data,j=p.isLoading,y=v[2].mutate,C=(0,a.useState)(!1),w=(0,n.Z)(C,2),N=w[0],Z=w[1],k=(0,a.useState)(!1),S=(0,n.Z)(k,2),I=S[0],O=S[1];return j?(0,c.jsx)("div",{className:"aspect-square max-w-[20rem] animate-pulse bg-slate-100 dark:bg-slate-700"}):(0,c.jsxs)(c.Fragment,{children:[b?(0,c.jsx)("div",{className:"flex",children:(0,c.jsxs)("div",{className:"relative mr-auto",children:[(0,c.jsx)("button",{className:"absolute top-2 right-2 rounded-full bg-white p-2",onClick:function(e){return e.preventDefault(),Z(!0),!1},children:(0,c.jsx)(u,{className:"h-4 w-4 text-black"})}),(0,c.jsx)("button",{className:"absolute bottom-2 right-2 rounded-full bg-red-200 p-2",onClick:function(){return O(!0),!1},children:(0,c.jsx)(d.Z,{className:"h-4 w-4 text-black"})}),(0,c.jsx)("img",{src:b,alt:b,className:"max-h-[20rem]",onClick:function(){Z(!0)}})]})}):(0,c.jsxs)("div",{className:"relative flex aspect-video max-w-[20rem] cursor-pointer bg-slate-100 dark:bg-slate-700",onClick:function(e){e.preventDefault(),Z(!0)},children:[(0,c.jsx)(o.Z,{className:"m-auto h-8 w-8"}),(0,c.jsx)("p",{className:"absolute inset-0 top-auto pb-5 text-center text-slate-400",children:(0,i.t)("No image selected")})]}),(0,c.jsx)(m.Z,{acl:r,isOpen:N,targetDrive:t,title:(0,i.t)("Insert image"),confirmText:(0,i.t)("Add"),onCancel:function(){return Z(!1)},onConfirm:function(e){f({target:{name:h,value:e}}),Z(!1)}}),(0,c.jsx)(l.Z,{title:"Remove Current Image",confirmText:"Permanently remove",needConfirmation:I,onConfirm:function(){y({fileId:"string"===typeof x?x:void 0,targetDrive:t},{onSuccess:function(){f({target:{name:h,value:""}})}})},onCancel:function(){O(!1)},children:(0,c.jsx)("p",{className:"text-sm text-gray-500",children:(0,i.t)("Are you sure you want to remove the current file? This action cannot be undone.")})})]})}},3225:function(e,t,r){var n=r(1413),a=r(184);t.Z=function(e){return(0,a.jsx)("select",(0,n.Z)((0,n.Z)({},e),{},{className:"w-full rounded border border-gray-300 bg-white py-1 px-3 text-base leading-8 text-gray-700 outline-none transition-colors duration-200 ease-in-out focus:border-indigo-500 focus:ring-2 focus:ring-indigo-200 dark:border-gray-700 dark:bg-gray-800 dark:text-gray-100 ".concat(e.className),children:e.children}))}},8491:function(e,t,r){var n=r(184);t.Z=function(e){var t=e.title,r=e.className,a=e.children,i=e.isOpaqueBg,s=void 0!==i&&i;return(0,n.jsxs)("section",{className:"my-5 rounded-md ".concat(s?"rounded-lg border-[1px] border-gray-200 border-opacity-80 dark:border-gray-700":"bg-slate-50 dark:bg-slate-800"," px-5  dark:text-slate-300 ").concat(null!==r&&void 0!==r?r:""),children:[t?(0,n.jsx)("div",{className:"relative border-b-[1px] border-gray-200 border-opacity-80 py-5 transition-all duration-300 dark:border-gray-700",children:(0,n.jsx)("h3",{className:"text-2xl dark:text-white",children:t})}):null,(0,n.jsx)("div",{className:"py-5 ",children:a})]})}},8088:function(e,t,r){var n=r(4165),a=r(5861),i=r(7408),s=r(1803),l=r(6117),o={alias:s.Ib.BlogMainContentDriveId.toString(),type:s.Hm.DriveType.toString()};t.Z=function(e,t){var r=(0,l.Z)().getSharedSecret,c=(0,i.useQueryClient)(),u=new s.KU({api:s.Ii.Owner,sharedSecret:r()}),d=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t,r){return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(void 0!==t&&""!==t){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.mediaProvider.getDecryptedImageUrl(null!==r&&void 0!==r?r:o,t);case 4:return e.abrupt("return",e.sent);case 5:case"end":return e.stop()}}),e)})));return function(t,r){return e.apply(this,arguments)}}(),m=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a,i,l,c,d,m;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=t.bytes,a=t.targetDrive,i=void 0===a?o:a,l=t.acl,c=void 0===l?{requiredSecurityGroup:s.hh.Anonymous}:l,d=t.fileId,m=void 0===d?void 0:d,e.next=3,u.mediaProvider.uploadImage(i,void 0,c,r,m);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),f=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){var r,a,i;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return r=t.targetDrive,a=void 0===r?o:r,i=t.fileId,e.next=3,u.mediaProvider.removeImage(i,a);case 3:return e.abrupt("return",e.sent);case 4:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return[(0,i.useQuery)(["image",e,t],(function(){return d(e,t)}),{refetchOnMount:!1,refetchOnWindowFocus:!1,staleTime:1/0}),(0,i.useMutation)(m,{onSuccess:function(e,t){var r;t.fileId?c.removeQueries(["image",t.fileId,null!==(r=t.targetDrive)&&void 0!==r?r:o]):c.removeQueries(["image"])}}),(0,i.useMutation)(f,{onSuccess:function(e,t){var r;t.fileId?c.removeQueries(["image",t.fileId,null!==(r=t.targetDrive)&&void 0!==r?r:o]):c.removeQueries(["image"])}})]}},9077:function(e,t,r){var n=r(4165),a=r(5861),i=r(7408),s=r(1803),l=r(6117),o=r(1695);t.Z=function(e){var t=e.profileId,r=e.attributeId,c=(0,l.Z)().getSharedSecret,u=new s.KU({api:s.Ii.Owner,sharedSecret:c()}),d=(0,i.useQueryClient)(),m=(0,o.Z)().publish.mutate,f=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t,r){var a;return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(t&&r){e.next=2;break}return e.abrupt("return");case 2:return e.next=4,u.profileDataProvider.getAttribute(t,r);case 4:return a=e.sent,e.abrupt("return",a);case 6:case"end":return e.stop()}}),e)})));return function(t,r){return e.apply(this,arguments)}}(),x=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:return e.next=2,u.profileDataProvider.saveAttribute(t);case 2:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}(),h=function(){var e=(0,a.Z)((0,n.Z)().mark((function e(t){return(0,n.Z)().wrap((function(e){for(;;)switch(e.prev=e.next){case 0:if(!t.fileId){e.next=5;break}return e.next=3,u.profileDataProvider.removeAttribute(t.sectionId,t.fileId);case 3:e.next=6;break;case 5:console.log("error...");case 6:case"end":return e.stop()}}),e)})));return function(t){return e.apply(this,arguments)}}();return[(0,i.useQuery)(["attribute",t,r],(function(){return f(t,r)}),{refetchOnMount:!1,refetchOnWindowFocus:!1}),(0,i.useMutation)(x,{onSuccess:function(e,t){t.id?d.invalidateQueries(["attribute",t.profileId,t.id]):d.invalidateQueries(["attribute"]),d.removeQueries(["attributes",t.profileId,t.sectionId]),m()}}),(0,i.useMutation)(h,{onSuccess:function(e,t){t.id?d.invalidateQueries(["attribute",t.profileId,t.id]):d.invalidateQueries(["attribute"]),d.removeQueries(["attributes",t.profileId,t.sectionId]),m()}})]}},4942:function(e,t,r){function n(e,t,r){return t in e?Object.defineProperty(e,t,{value:r,enumerable:!0,configurable:!0,writable:!0}):e[t]=r,e}r.d(t,{Z:function(){return n}})},1413:function(e,t,r){r.d(t,{Z:function(){return i}});var n=r(4942);function a(e,t){var r=Object.keys(e);if(Object.getOwnPropertySymbols){var n=Object.getOwnPropertySymbols(e);t&&(n=n.filter((function(t){return Object.getOwnPropertyDescriptor(e,t).enumerable}))),r.push.apply(r,n)}return r}function i(e){for(var t=1;t<arguments.length;t++){var r=null!=arguments[t]?arguments[t]:{};t%2?a(Object(r),!0).forEach((function(t){(0,n.Z)(e,t,r[t])})):Object.getOwnPropertyDescriptors?Object.defineProperties(e,Object.getOwnPropertyDescriptors(r)):a(Object(r)).forEach((function(t){Object.defineProperty(e,t,Object.getOwnPropertyDescriptor(r,t))}))}return e}},2878:function(e,t,r){r.d(t,{Z:function(){return F}});var n=function(e){var t=typeof e;return null!=e&&("object"==t||"function"==t)},a="object"==typeof global&&global&&global.Object===Object&&global,i="object"==typeof self&&self&&self.Object===Object&&self,s=a||i||Function("return this")(),l=function(){return s.Date.now()},o=/\s/;var c=function(e){for(var t=e.length;t--&&o.test(e.charAt(t)););return t},u=/^\s+/;var d=function(e){return e?e.slice(0,c(e)+1).replace(u,""):e},m=s.Symbol,f=Object.prototype,x=f.hasOwnProperty,h=f.toString,g=m?m.toStringTag:void 0;var v=function(e){var t=x.call(e,g),r=e[g];try{e[g]=void 0;var n=!0}catch(i){}var a=h.call(e);return n&&(t?e[g]=r:delete e[g]),a},p=Object.prototype.toString;var b=function(e){return p.call(e)},j=m?m.toStringTag:void 0;var y=function(e){return null==e?void 0===e?"[object Undefined]":"[object Null]":j&&j in Object(e)?v(e):b(e)};var C=function(e){return null!=e&&"object"==typeof e};var w=function(e){return"symbol"==typeof e||C(e)&&"[object Symbol]"==y(e)},N=/^[-+]0x[0-9a-f]+$/i,Z=/^0b[01]+$/i,k=/^0o[0-7]+$/i,S=parseInt;var I=function(e){if("number"==typeof e)return e;if(w(e))return NaN;if(n(e)){var t="function"==typeof e.valueOf?e.valueOf():e;e=n(t)?t+"":t}if("string"!=typeof e)return 0===e?e:+e;e=d(e);var r=Z.test(e);return r||k.test(e)?S(e.slice(2),r?2:8):N.test(e)?NaN:+e},O=Math.max,D=Math.min;var F=function(e,t,r){var a,i,s,o,c,u,d=0,m=!1,f=!1,x=!0;if("function"!=typeof e)throw new TypeError("Expected a function");function h(t){var r=a,n=i;return a=i=void 0,d=t,o=e.apply(n,r)}function g(e){return d=e,c=setTimeout(p,t),m?h(e):o}function v(e){var r=e-u;return void 0===u||r>=t||r<0||f&&e-d>=s}function p(){var e=l();if(v(e))return b(e);c=setTimeout(p,function(e){var r=t-(e-u);return f?D(r,s-(e-d)):r}(e))}function b(e){return c=void 0,x&&a?h(e):(a=i=void 0,o)}function j(){var e=l(),r=v(e);if(a=arguments,i=this,u=e,r){if(void 0===c)return g(u);if(f)return clearTimeout(c),c=setTimeout(p,t),h(u)}return void 0===c&&(c=setTimeout(p,t)),o}return t=I(t)||0,n(r)&&(m=!!r.leading,s=(f="maxWait"in r)?O(I(r.maxWait)||0,t):s,x="trailing"in r?!!r.trailing:x),j.cancel=function(){void 0!==c&&clearTimeout(c),d=0,a=u=i=c=void 0},j.flush=function(){return void 0===c?o:b(l())},j}}}]);
//# sourceMappingURL=864.761ed7ec.chunk.js.map