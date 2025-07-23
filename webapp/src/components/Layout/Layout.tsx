import React from 'react';
import { useAppSelector } from '../../hooks/redux';
import Sidebar from './Sidebar/Sidebar';
import Header from './Header/Header';
import StatusBar from './StatusBar/StatusBar';
import './Layout.css';

interface LayoutProps {
  children: React.ReactNode;
}

const Layout: React.FC<LayoutProps> = ({ children }) => {
  const { sidebarOpen } = useAppSelector((state) => state.ui);

  return (
    <div className="layout">
      <Header />
      <div className="layout-body">
        <Sidebar />
        <main className={`layout-main ${!sidebarOpen ? 'sidebar-collapsed' : ''}`}>
          {children}
        </main>
      </div>
      <StatusBar />
    </div>
  );
};

export default Layout;