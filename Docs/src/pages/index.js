import React from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';

import HomepageContent from '@site/src/components/HomepageContent.mdx';
import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={clsx('hero hero--primary', styles.heroBanner)}>
      <div className="container">
        <h1 className="hero__title">{siteConfig.title}</h1>
        <p className="hero__subtitle">{siteConfig.tagline}</p>
      </div>
    </header>
  );
}

export default function Home() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout title="Home" description={`${siteConfig.tagline}`}>
      <HomepageHeader />
      <main>
        <section className={clsx(styles.content)}>
          <div className="container">
            <HomepageContent />
          </div>
        </section>
      </main>
    </Layout>
  );
}
